using Aegis.Data.Entities;
using Aegis.Data.Services;
using Aegis.BotApi.Infrastructure.Auth;
using System.Security.Cryptography;

namespace Aegis.BotApi.Endpoints;

/// <summary>
/// Developer portal endpoints — lets users register client applications and receive
/// api_id / api_hash credentials, similar to Telegram's my.telegram.org/apps.
///
/// Authentication: callers must pass their Aegis session token in the
/// "X-Session-Token" header.  The server resolves the user identity from it.
/// </summary>
public static class DevPortalEndpoints
{
    public static IEndpointRouteBuilder MapDevPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/apps")
            .WithTags("Developer Portal");

        group.MapGet("/", ListApps)
            .WithName("ListApps")
            .WithSummary("List all registered applications for the authenticated user.");

        group.MapGet("/{appId:int}", GetApp)
            .WithName("GetApp")
            .WithSummary("Get details of a specific application.");

        group.MapPost("/", CreateApp)
            .WithName("CreateApp")
            .WithSummary("Register a new application and receive api_id + api_hash.");

        group.MapDelete("/{appId:int}", RevokeApp)
            .WithName("RevokeApp")
            .WithSummary("Revoke an application credential.");

        group.MapPost("/{appId:int}/reveal", RevealAppHash)
            .WithName("RevealAppHash")
            .WithSummary("Reveal full api_hash for a specific application.");

        return app;
    }

    // ── Request / response DTOs ──────────────────────────────────────────────

    public sealed record CreateAppRequest(
        string AppTitle,
        string ShortName,
        string? Description,
        string? Website,
        string Platform = "other");

    public sealed record AppResponse(
        int AppId,
        string AppHash,
        string AppTitle,
        string ShortName,
        string? Description,
        string? Website,
        string Platform,
        bool IsActive,
        DateTime CreatedAt,
        DateTime? LastUsedAt,
        DateTime? RevokedAt,
        string? ServerHandshakeSigningPublicKeyBase64 = null);

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static async Task<IResult> ListApps(
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc,
        IConfiguration configuration)
    {
        var authContext = await ResolveAuthContext(http, authSvc);
        if (authContext == null) return Results.Unauthorized();

        var signingPublicKey = ResolveServerHandshakeSigningPublicKeyBase64(configuration);
        var apps = await svc.GetUserAppsAsync(authContext.UserId);
        return Results.Ok(apps.Select(a => ToDto(a, includeFullHash: false, signingPublicKey)));
    }

    private static async Task<IResult> GetApp(
        int appId,
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc,
        IConfiguration configuration)
    {
        var authContext = await ResolveAuthContext(http, authSvc);
        if (authContext == null) return Results.Unauthorized();

        var app = await svc.GetAppAsync(appId, authContext.UserId);
        return app == null
            ? Results.NotFound()
            : Results.Ok(ToDto(app, includeFullHash: false, ResolveServerHandshakeSigningPublicKeyBase64(configuration)));
    }

    private static async Task<IResult> CreateApp(
        CreateAppRequest req,
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc,
        ICsrfProtectionService csrfSvc,
        IConfiguration configuration)
    {
        var authContext = await ResolveAuthContext(http, authSvc);
        if (authContext == null) return Results.Unauthorized();
        if (!ValidateCsrf(http, csrfSvc, authContext.SessionToken)) return Results.Unauthorized();

        try
        {
            var created = await svc.CreateAppAsync(
                authContext.UserId,
                req.AppTitle,
                req.ShortName,
                req.Description,
                req.Website,
                req.Platform);

            return Results.Created(
                $"/api/apps/{created.AppId}",
                ToDto(
                    created,
                    includeFullHash: true,
                    ResolveServerHandshakeSigningPublicKeyBase64(configuration)));
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

    private static async Task<IResult> RevokeApp(
        int appId,
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc,
        ICsrfProtectionService csrfSvc)
    {
        var authContext = await ResolveAuthContext(http, authSvc);
        if (authContext == null) return Results.Unauthorized();
        if (!ValidateCsrf(http, csrfSvc, authContext.SessionToken)) return Results.Unauthorized();

        var revoked = await svc.RevokeAppAsync(appId, authContext.UserId);
        return revoked ? Results.Ok(new { ok = true }) : Results.NotFound();
    }

    private static async Task<IResult> RevealAppHash(
        int appId,
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc,
        ICsrfProtectionService csrfSvc,
        IConfiguration configuration)
    {
        var authContext = await ResolveAuthContext(http, authSvc);
        if (authContext == null) return Results.Unauthorized();
        if (!ValidateCsrf(http, csrfSvc, authContext.SessionToken)) return Results.Unauthorized();

        var app = await svc.GetAppAsync(appId, authContext.UserId);
        if (app == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            appId = app.AppId,
            appHash = app.AppHash,
            serverHandshakeSigningPublicKeyBase64 = ResolveServerHandshakeSigningPublicKeyBase64(configuration)
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the authenticated user id from the X-Session-Token header.
    /// Returns null when the token is missing or invalid.
    /// </summary>
    private static async Task<AuthContext?> ResolveAuthContext(HttpContext http, IUserAuthenticationService authSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var session = await authSvc.AuthenticateUserByTokenAsync(token);
        if (session == null)
        {
            return null;
        }

        return new AuthContext(session.UserId, token);
    }

    private static bool ValidateCsrf(HttpContext http, ICsrfProtectionService csrfSvc, string sessionToken)
    {
        var csrf = http.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(csrf) && csrfSvc.ValidateToken(sessionToken, csrf);
    }

    private static string MaskHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 8 ? value : $"{value[..8]}...";
    }

    private static AppResponse ToDto(
        AppCredential a,
        bool includeFullHash,
        string? serverHandshakeSigningPublicKeyBase64) => new(
        a.AppId,
        a.IsActive ? (includeFullHash ? a.AppHash : MaskHash(a.AppHash)) : string.Empty,
        a.AppTitle,
        a.ShortName,
        a.Description,
        a.Website,
        a.Platform,
        a.IsActive,
        a.CreatedAt,
        a.LastUsedAt,
        a.RevokedAt,
        serverHandshakeSigningPublicKeyBase64);

    private static string? ResolveServerHandshakeSigningPublicKeyBase64(IConfiguration configuration)
    {
        var explicitPublicKey = configuration["BotApi:ServerHandshakeSigningPublicKeyBase64"];
        if (!string.IsNullOrWhiteSpace(explicitPublicKey))
        {
            return explicitPublicKey;
        }

        var signingPrivateKeyBase64 = configuration["ProtocolSecurity:HandshakeSigningPrivateKeyBase64"];
        if (string.IsNullOrWhiteSpace(signingPrivateKeyBase64))
        {
            return null;
        }

        try
        {
            var privateKey = Convert.FromBase64String(signingPrivateKeyBase64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(privateKey, out _);
            var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
            return Convert.ToBase64String(publicKey);
        }
        catch
        {
            return null;
        }
    }

    private sealed record AuthContext(ulong UserId, string SessionToken);
}
