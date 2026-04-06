using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.BotApi.Infrastructure.Auth;
using Aegis.BotApi.Infrastructure.Mail;
using Microsoft.Extensions.Caching.Distributed;

namespace Aegis.BotApi.Endpoints;

/// <summary>
/// HTTP authentication endpoints for the developer portal.
/// Lets users log in/out and register via a web browser.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Login with username and password. Returns a session token.");

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("Logout and invalidate the current session.");

        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Register a new account and send an email verification code.");

        group.MapPost("/verify-email", VerifyEmail)
            .WithName("VerifyEmail")
            .WithSummary("Verify email with code sent during registration.");

        group.MapPost("/request-password-reset", RequestPasswordReset)
            .WithName("RequestPasswordReset")
            .WithSummary("Send a password reset code to email.");

        group.MapPost("/reset-password", ResetPassword)
            .WithName("ResetPassword")
            .WithSummary("Reset password by email code.");

        group.MapPost("/request-login-code", RequestLoginCode)
            .WithName("RequestLoginCode")
            .WithSummary("Send one-time login code to email.");

        group.MapPost("/login-with-code", LoginWithCode)
            .WithName("LoginWithCode")
            .WithSummary("Login by email one-time code.");

        group.MapPost("/2fa/setup", SetupTwoFactor)
            .WithName("SetupTwoFactor")
            .WithSummary("Generate a TOTP secret and 20-word recovery phrase.");

        group.MapPost("/2fa/enable", EnableTwoFactor)
            .WithName("EnableTwoFactor")
            .WithSummary("Enable 2FA by confirming current TOTP code.");

        group.MapPost("/2fa/disable", DisableTwoFactor)
            .WithName("DisableTwoFactor")
            .WithSummary("Disable 2FA by TOTP code or recovery phrase.");

        group.MapGet("/me", Me)
            .WithName("Me")
            .WithSummary("Return info about the currently authenticated user.");

        return app;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public sealed record LoginRequest(string Username, string Password, string? TwoFactorCode = null, string? RecoveryPhrase = null);

    public sealed record RegisterRequest(
        string Username,
        string Email,
        string Password,
        // PublicKey is optional for portal registrations.
        // When absent, the server assigns a legacy placeholder key.
        string? PublicKey = null);

    public sealed record AuthResponse(string SessionToken, string CsrfToken, ulong UserId, string Username);
    public sealed record RegisterResponse(bool RequiresEmailVerification, string Email);
    public sealed record VerifyEmailRequest(string Email, string Code);
    public sealed record RequestPasswordResetRequest(string Email);
    public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
    public sealed record RequestLoginCodeRequest(string Email);
    public sealed record LoginWithCodeRequest(string Email, string Code);
    public sealed record SetupTwoFactorResponse(string Secret, string OtpAuthUri, string RecoveryPhrase);
    public sealed record TwoFactorCodeRequest(string Code);
    public sealed record DisableTwoFactorRequest(string Code, string? RecoveryPhrase = null);

    public sealed record MeResponse(ulong UserId, string Username, string? DisplayName);

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static async Task<IResult> Login(
        LoginRequest req,
        HttpContext http,
        IUserAuthenticationService authSvc,
        IDistributedCache cache,
        ICsrfProtectionService csrfSvc)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "Username and password are required." });

        var remoteIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var loginRateKey = $"auth:login:{remoteIp}:{req.Username.Trim().ToLowerInvariant()}";
        if (await IsRateLimitedAsync(cache, loginRateKey, maxAttempts: 10, window: TimeSpan.FromMinutes(10)))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var detailed = await authSvc.AuthenticateUserWithStatusAsync(
            req.Username,
            req.Password,
            clientInfo: "portal/web",
            ipAddress: remoteIp,
            twoFactorCode: req.TwoFactorCode,
            recoveryPhrase: req.RecoveryPhrase);

        if (!detailed.Success || detailed.User == null || detailed.Session == null)
        {
            await RegisterFailedAttemptAsync(cache, loginRateKey, TimeSpan.FromMinutes(10));
            return Results.Unauthorized();
        }

        var user = detailed.User;
        var session = detailed.Session;
        await ClearRateLimitAsync(cache, loginRateKey);

        var csrfToken = csrfSvc.IssueToken(session.SessionToken);
        return Results.Ok(new AuthResponse(session.SessionToken, csrfToken, user.Id, user.Username));
    }

    private static async Task<IResult> Logout(
        HttpContext http,
        IUserAuthenticationService authSvc,
        ICsrfProtectionService csrfSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        var csrfToken = http.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(csrfToken) || !csrfSvc.ValidateToken(token, csrfToken))
            return Results.Unauthorized();

        await authSvc.LogoutAsync(token);
        csrfSvc.Revoke(token);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Register(
        RegisterRequest req,
        HttpContext http,
        IUserRegistrationService regSvc,
        IDistributedCache cache,
        IEmailChallengeService challengeSvc,
        IEmailSender mailSender)
    {
        if (string.IsNullOrWhiteSpace(req.Username))
            return Results.BadRequest(new { error = "Username is required." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters." });

        var remoteIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var registerRateKey = $"auth:register:{remoteIp}";
        if (await IsRateLimitedAsync(cache, registerRateKey, maxAttempts: 20, window: TimeSpan.FromHours(1)))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        try
        {
            await RegisterFailedAttemptAsync(cache, registerRateKey, TimeSpan.FromHours(1));

            var user = await regSvc.RegisterUserAsync(
                req.Username,
                req.Email ?? string.Empty,
                req.Password,
                req.PublicKey ?? string.Empty,
                isEmailVerified: false);

            var code = await challengeSvc.IssueCodeAsync(
                EmailChallengePurpose.VerifyEmail,
                user.Email,
                TimeSpan.FromMinutes(20));

            await mailSender.SendAsync(
                user.Email,
                "Twospace email verification",
                $"Your verification code is: {code}\n\nIt expires in 20 minutes.");

            return Results.Created(
                "/api/auth/verify-email",
                new RegisterResponse(true, user.Email));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async Task<IResult> VerifyEmail(
        VerifyEmailRequest req,
        IUserRepository userRepo,
        IEmailChallengeService challengeSvc)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
        {
            return Results.BadRequest(new { error = "Email and code are required." });
        }

        var ok = await challengeSvc.ValidateCodeAsync(EmailChallengePurpose.VerifyEmail, req.Email, req.Code);
        if (!ok)
        {
            return Results.BadRequest(new { error = "Invalid or expired verification code." });
        }

        var user = await userRepo.GetByEmailAsync(UserRegistrationService.NormalizeEmail(req.Email));
        if (user == null)
        {
            return Results.NotFound(new { error = "User not found." });
        }

        user.IsEmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await userRepo.UpdateAsync(user);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> RequestPasswordReset(
        RequestPasswordResetRequest req,
        IUserRepository userRepo,
        IEmailChallengeService challengeSvc,
        IEmailSender mailSender)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
        {
            return Results.BadRequest(new { error = "Email is required." });
        }

        var normalizedEmail = UserRegistrationService.NormalizeEmail(req.Email);
        var user = await userRepo.GetByEmailAsync(normalizedEmail);
        if (user != null)
        {
            var code = await challengeSvc.IssueCodeAsync(EmailChallengePurpose.ResetPassword, normalizedEmail, TimeSpan.FromMinutes(20));
            await mailSender.SendAsync(normalizedEmail, "Twospace password reset", $"Your password reset code is: {code}\n\nIt expires in 20 minutes.");
        }

        // Return success even if email is not found to avoid user enumeration.
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest req,
        IUserRepository userRepo,
        IUserAuthenticationService authSvc,
        IEmailChallengeService challengeSvc)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.NewPassword))
        {
            return Results.BadRequest(new { error = "Email, code and newPassword are required." });
        }

        var normalizedEmail = UserRegistrationService.NormalizeEmail(req.Email);
        var codeOk = await challengeSvc.ValidateCodeAsync(EmailChallengePurpose.ResetPassword, normalizedEmail, req.Code);
        if (!codeOk)
        {
            return Results.BadRequest(new { error = "Invalid or expired reset code." });
        }

        var user = await userRepo.GetByEmailAsync(normalizedEmail);
        if (user == null)
        {
            return Results.NotFound(new { error = "User not found." });
        }

        var changed = await authSvc.SetPasswordAsync(user.Id, req.NewPassword);
        if (!changed)
        {
            return Results.BadRequest(new { error = "Invalid password." });
        }

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> RequestLoginCode(
        RequestLoginCodeRequest req,
        IUserRepository userRepo,
        IEmailChallengeService challengeSvc,
        IEmailSender mailSender)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
        {
            return Results.BadRequest(new { error = "Email is required." });
        }

        var normalizedEmail = UserRegistrationService.NormalizeEmail(req.Email);
        var user = await userRepo.GetByEmailAsync(normalizedEmail);
        if (user != null && user.IsEmailVerified && user.IsActive)
        {
            var code = await challengeSvc.IssueCodeAsync(EmailChallengePurpose.LoginCode, normalizedEmail, TimeSpan.FromMinutes(10));
            await mailSender.SendAsync(normalizedEmail, "Twospace login code", $"Your login code is: {code}\n\nIt expires in 10 minutes.");
        }

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> LoginWithCode(
        LoginWithCodeRequest req,
        HttpContext http,
        IUserRepository userRepo,
        IUserAuthenticationService authSvc,
        IEmailChallengeService challengeSvc,
        ICsrfProtectionService csrfSvc)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
        {
            return Results.BadRequest(new { error = "Email and code are required." });
        }

        var normalizedEmail = UserRegistrationService.NormalizeEmail(req.Email);
        var codeOk = await challengeSvc.ValidateCodeAsync(EmailChallengePurpose.LoginCode, normalizedEmail, req.Code);
        if (!codeOk)
        {
            return Results.Unauthorized();
        }

        var user = await userRepo.GetByEmailAsync(normalizedEmail);
        if (user == null || !user.IsActive || !user.IsEmailVerified)
        {
            return Results.Unauthorized();
        }

        if (user.TwoFactorEnabled)
        {
            return Results.BadRequest(new { error = "Two-factor is enabled. Use password login with 2FA code." });
        }

        var remoteIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (_, session) = await authSvc.CreateSessionAsync(user, "portal/web/email-code", remoteIp);
        var csrfToken = csrfSvc.IssueToken(session.SessionToken);
        return Results.Ok(new AuthResponse(session.SessionToken, csrfToken, user.Id, user.Username));
    }

    private static async Task<IResult> SetupTwoFactor(
        HttpContext http,
        IUserAuthenticationService authSvc,
        IUserTwoFactorService twoFactorSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Unauthorized();
        }

        var session = await authSvc.AuthenticateUserByTokenAsync(token);
        if (session == null)
        {
            return Results.Unauthorized();
        }

        var setup = await twoFactorSvc.BeginSetupAsync(session.UserId, "Twospace");
        return Results.Ok(new SetupTwoFactorResponse(setup.Secret, setup.OtpauthUri, setup.RecoveryPhrase));
    }

    private static async Task<IResult> EnableTwoFactor(
        TwoFactorCodeRequest req,
        HttpContext http,
        IUserAuthenticationService authSvc,
        IUserTwoFactorService twoFactorSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Unauthorized();
        }

        var session = await authSvc.AuthenticateUserByTokenAsync(token);
        if (session == null)
        {
            return Results.Unauthorized();
        }

        var ok = await twoFactorSvc.EnableAsync(session.UserId, req.Code);
        return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = "Invalid TOTP code." });
    }

    private static async Task<IResult> DisableTwoFactor(
        DisableTwoFactorRequest req,
        HttpContext http,
        IUserAuthenticationService authSvc,
        IUserTwoFactorService twoFactorSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Unauthorized();
        }

        var session = await authSvc.AuthenticateUserByTokenAsync(token);
        if (session == null)
        {
            return Results.Unauthorized();
        }

        var ok = await twoFactorSvc.DisableAsync(session.UserId, req.Code, req.RecoveryPhrase);
        return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = "Invalid code or recovery phrase." });
    }

    private static async Task<IResult> Me(
        HttpContext http,
        IUserAuthenticationService authSvc,
        IUserRepository userRepo)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return Results.Unauthorized();

        var session = await authSvc.AuthenticateUserByTokenAsync(token);
        if (session == null)
            return Results.Unauthorized();

        var user = await userRepo.GetByIdAsync(session.UserId);
        if (user == null)
            return Results.Unauthorized();

        return Results.Ok(new MeResponse(session.UserId, user.Username, user.DisplayName));
    }

    private static async Task<bool> IsRateLimitedAsync(
        IDistributedCache cache,
        string key,
        int maxAttempts,
        TimeSpan window)
    {
        var value = await cache.GetStringAsync(key);
        if (!int.TryParse(value, out var attempts))
        {
            return false;
        }

        return attempts >= maxAttempts;
    }

    private static async Task RegisterFailedAttemptAsync(
        IDistributedCache cache,
        string key,
        TimeSpan window)
    {
        var value = await cache.GetStringAsync(key);
        var attempts = int.TryParse(value, out var parsed) ? parsed : 0;
        attempts++;

        await cache.SetStringAsync(
            key,
            attempts.ToString(),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = window
            });
    }

    private static Task ClearRateLimitAsync(IDistributedCache cache, string key)
        => cache.RemoveAsync(key);
}
