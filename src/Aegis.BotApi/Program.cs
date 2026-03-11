using Aegis.BotApi;
using Aegis.BotApi.Application.Abstractions;
using Aegis.BotApi.Application.UseCases;
using Aegis.BotApi.Endpoints;
using Aegis.BotApi.Infrastructure.Auth;
using Aegis.BotApi.Mappers;
using Aegis.BotApi.Services;
using Aegis.Data;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

BotApiStartupValidation.Validate(builder.Configuration, builder.Environment);

builder.Services.Configure<BotApiOptions>(builder.Configuration.GetSection(BotApiOptions.SectionName));

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
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IBotRepository, BotRepository>();
builder.Services.AddScoped<IBotTokenRepository, BotTokenRepository>();
builder.Services.AddScoped<IBotConversationStateRepository, BotConversationStateRepository>();

builder.Services.AddScoped<IUserSearchService, UserSearchService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IBotManagementService, BotManagementService>();

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

app.MapBotEndpoints();

app.Run();
