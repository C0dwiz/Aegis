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
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BotApiOptions>(builder.Configuration.GetSection(BotApiOptions.SectionName));

builder.Services.AddDbContext<AegisDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? builder.Configuration["Database:ConnectionString"]
        ?? "Data Source=aegis.db";

    options.UseSqlite(connectionString);
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

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapBotEndpoints();

app.Run();
