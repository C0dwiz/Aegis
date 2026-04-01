using Aegis.BotApi;
using Aegis.BotApi.Application.Abstractions;
using Aegis.BotApi.Application.UseCases;
using Aegis.BotApi.Endpoints;
using Aegis.BotApi.Infrastructure.Auth;
using Aegis.BotApi.Mappers;
using Aegis.BotApi.Services;
using Aegis.Crypto;
using Aegis.Data;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

BotApiStartupValidation.Validate(builder.Configuration, builder.Environment);

builder.Services.Configure<BotApiOptions>(builder.Configuration.GetSection(BotApiOptions.SectionName));
builder.Services.Configure<ElasticsearchOptions>(builder.Configuration.GetSection(ElasticsearchOptions.SectionName));
builder.Services.Configure<Aegis.Common.Configuration.IdGeneratorOptions>(
    builder.Configuration.GetSection(Aegis.Common.Configuration.IdGeneratorOptions.SectionName));

// Register distributed ID generator with configurable nodeId for multi-instance deployments
builder.Services.AddScoped<Aegis.Data.Utils.FastIdGenerator>(serviceProvider =>
{
    var idGeneratorOptions = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<Aegis.Common.Configuration.IdGeneratorOptions>>()
        .Value;
    return new Aegis.Data.Utils.FastIdGenerator(idGeneratorOptions.NodeId);
});
builder.Services.AddHttpClient<ElasticsearchUserSearchIndexService>();
builder.Services.AddScoped<IUserSearchIndexService>(serviceProvider =>
{
    var searchOptions = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<ElasticsearchOptions>>()
        .Value;

    if (!searchOptions.Enabled)
    {
        return new NoOpUserSearchIndexService();
    }

    return serviceProvider.GetRequiredService<ElasticsearchUserSearchIndexService>();
});

var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "aegis:botapi:";
});

builder.Services.AddDbContextPool<AegisDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? builder.Configuration["Database:ConnectionString"]
        ?? "Host=localhost;Port=5432;Database=aegis;Username=aegis;Password=aegis";

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();
builder.Services.AddSingleton<AegisCryptoProvider>();
builder.Services.AddSingleton<Aegis.Common.ICryptoProvider>(sp =>
    new CommonCryptoProviderAdapter(sp.GetRequiredService<AegisCryptoProvider>()));
// ZoneTree must be Singleton - it holds open file handles
builder.Services.AddSingleton<IMessageRepository>(_ => new ZoneTreeMessageRepository("zonetree-messages-db"));
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IBotRepository, BotRepository>();
builder.Services.AddScoped<IBotTokenRepository, BotTokenRepository>();
builder.Services.AddScoped<IBotConversationStateRepository, BotConversationStateRepository>();
builder.Services.AddScoped<IAppCredentialRepository, AppCredentialRepository>();

builder.Services.AddScoped<IUserSearchService, UserSearchService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IBotManagementService, BotManagementService>();
builder.Services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
builder.Services.AddScoped<IAppCredentialService, AppCredentialService>();

builder.Services.AddScoped<IBotAuthenticator, BotAuthenticator>();

builder.Services.AddScoped<IBotMessageUseCase, BotMessageUseCase>();
builder.Services.AddScoped<BotRequestMapper>();

builder.Services.AddSingleton<ChatIdResolver>();
builder.Services.AddSingleton<RichContentFormatter>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var avatarBaseDirectory = builder.Configuration["AvatarStorage:BaseDirectory"] ?? "/tmp/aegis-media/avatars";
Directory.CreateDirectory(avatarBaseDirectory);

// Serve developer portal and other static assets from wwwroot.
// UseDefaultFiles must come before UseStaticFiles to serve index.html at /portal/.
app.UseDefaultFiles();
app.UseStaticFiles();

// Convenience redirect: GET /portal → /portal/index.html
app.MapGet("/portal", () => Results.Redirect("/portal/index.html"));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(avatarBaseDirectory),
    RequestPath = "/media/avatars"
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async httpContext =>
    {
        await Results.Problem("Unhandled server error", statusCode: 500).ExecuteAsync(httpContext);
    });
});

app.MapGet("/health", () => Results.Ok(new { ok = true }));
app.MapGet("/health/live", () => Results.Ok(new { ok = true, service = "botapi" }));
app.MapGet("/health/ready", async (AegisDbContext db, IDistributedCache cache, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    if (!dbOk)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var probeKey = $"health:redis:{Guid.NewGuid():N}";
    await cache.SetStringAsync(
        probeKey,
        "ok",
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
        },
        ct);

    var redisValue = await cache.GetStringAsync(probeKey, ct);
    var redisOk = string.Equals(redisValue, "ok", StringComparison.Ordinal);

    if (!redisOk)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new { ok = true, db = true, redis = true });
});

app.MapAuthEndpoints();
app.MapBotEndpoints();
app.MapDevPortalEndpoints();

app.Run();
