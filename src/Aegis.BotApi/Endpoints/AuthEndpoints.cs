using Aegis.Data.Repositories;
using Aegis.Data.Services;

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
            .WithSummary("Register a new account. LoginRequest credentials are returned on success.");

        group.MapGet("/me", Me)
            .WithName("Me")
            .WithSummary("Return info about the currently authenticated user.");

        return app;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public sealed record LoginRequest(string Username, string Password);

    public sealed record RegisterRequest(
        string Username,
        string Email,
        string Password,
        // PublicKey is optional for portal registrations.
        // When absent, the server assigns a legacy placeholder key.
        string? PublicKey = null);

    public sealed record AuthResponse(string SessionToken, ulong UserId, string Username);

    public sealed record MeResponse(ulong UserId, string Username, string? DisplayName);

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static async Task<IResult> Login(
        LoginRequest req,
        HttpContext http,
        IUserAuthenticationService authSvc)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "Username and password are required." });

        var result = await authSvc.AuthenticateUserAsync(
            req.Username,
            req.Password,
            clientInfo: "portal/web",
            ipAddress: http.Connection.RemoteIpAddress?.ToString());

        if (result == null)
            return Results.Unauthorized();

        var (user, session) = result.Value;
        return Results.Ok(new AuthResponse(session.SessionToken, user.Id, user.Username));
    }

    private static async Task<IResult> Logout(
        HttpContext http,
        IUserAuthenticationService authSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return Results.Unauthorized();

        await authSvc.LogoutAsync(token);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> Register(
        RegisterRequest req,
        HttpContext http,
        IUserRegistrationService regSvc,
        IUserAuthenticationService authSvc)
    {
        if (string.IsNullOrWhiteSpace(req.Username))
            return Results.BadRequest(new { error = "Username is required." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters." });

        try
        {
            var user = await regSvc.RegisterUserAsync(
                req.Username,
                req.Email ?? string.Empty,
                req.Password,
                req.PublicKey ?? string.Empty);

            var result = await authSvc.AuthenticateUserAsync(
                req.Username,
                req.Password,
                clientInfo: "portal/web/register",
                ipAddress: http.Connection.RemoteIpAddress?.ToString());

            if (result == null)
                return Results.Problem("Registration succeeded but login failed.", statusCode: 500);

            var (_, session) = result.Value;
            return Results.Created(
                $"/portal/dashboard.html",
                new AuthResponse(session.SessionToken, user.Id, user.Username));
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
}
