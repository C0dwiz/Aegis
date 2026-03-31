using Aegis.Data.Entities;
using Aegis.Data.Services;

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
        DateTime? RevokedAt);

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static async Task<IResult> ListApps(
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc)
    {
        var userId = await ResolveUserId(http, authSvc);
        if (userId == null) return Results.Unauthorized();

        var apps = await svc.GetUserAppsAsync(userId.Value);
        return Results.Ok(apps.Select(ToDto));
    }

    private static async Task<IResult> GetApp(
        int appId,
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc)
    {
        var userId = await ResolveUserId(http, authSvc);
        if (userId == null) return Results.Unauthorized();

        var app = await svc.GetAppAsync(appId, userId.Value);
        return app == null ? Results.NotFound() : Results.Ok(ToDto(app));
    }

    private static async Task<IResult> CreateApp(
        CreateAppRequest req,
        HttpContext http,
        IAppCredentialService svc,
        IUserAuthenticationService authSvc)
    {
        var userId = await ResolveUserId(http, authSvc);
        if (userId == null) return Results.Unauthorized();

        try
        {
            var created = await svc.CreateAppAsync(
                userId.Value,
                req.AppTitle,
                req.ShortName,
                req.Description,
                req.Website,
                req.Platform);

            return Results.Created($"/api/apps/{created.AppId}", ToDto(created));
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
        IUserAuthenticationService authSvc)
    {
        var userId = await ResolveUserId(http, authSvc);
        if (userId == null) return Results.Unauthorized();

        var revoked = await svc.RevokeAppAsync(appId, userId.Value);
        return revoked ? Results.Ok(new { ok = true }) : Results.NotFound();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the authenticated user id from the X-Session-Token header.
    /// Returns null when the token is missing or invalid.
    /// </summary>
    private static async Task<ulong?> ResolveUserId(HttpContext http, IUserAuthenticationService authSvc)
    {
        var token = http.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var session = await authSvc.AuthenticateUserByTokenAsync(token);
        return session?.UserId;
    }

    private static AppResponse ToDto(AppCredential a) => new(
        a.AppId,
        // Only return the hash to the owner on creation; subsequent GETs mask it
        a.IsActive ? a.AppHash : string.Empty,
        a.AppTitle,
        a.ShortName,
        a.Description,
        a.Website,
        a.Platform,
        a.IsActive,
        a.CreatedAt,
        a.LastUsedAt,
        a.RevokedAt);
}
