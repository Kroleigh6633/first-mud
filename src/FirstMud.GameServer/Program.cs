using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using FirstMud.Infrastructure;
using FirstMud.Infrastructure.Neo4j;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("GameClient", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// Infrastructure repositories (EF + Neo4j)
builder.Services.AddInfrastructure(builder.Configuration);

// Neo4j driver + quest graph repository
// TODO: Move Neo4j registration into AddInfrastructure once Neo4j config is standardised
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var uri  = cfg["Neo4j:Uri"]      ?? "bolt://localhost:7687";
    var user = cfg["Neo4j:Username"] ?? "neo4j";
    var pass = cfg["Neo4j:Password"] ?? "password";
    return new Neo4jDriverWrapper(uri, user, pass);
});
builder.Services.AddScoped<IQuestGraphRepository, QuestGraphRepository>();

// Game services
builder.Services.AddSingleton<AiPlayerService>();
builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());
builder.Services.AddScoped<CommandDispatcher>();
builder.Services.AddScoped<WorldStateService>();

// TODO: Register Application layer services once implemented:
// builder.Services.AddScoped<CraftingService>();
// builder.Services.AddScoped<QuestService>();
// builder.Services.AddScoped<CombatService>();
// builder.Services.AddScoped<CompanionService>();
// builder.Services.AddScoped<PlayerService>();

var app = builder.Build();

// Trigger AiPlayerService initialization (singleton — ensures it's created on startup)
app.Services.GetRequiredService<AiPlayerService>();

app.UseCors("GameClient");
app.MapHub<GameHub>("/gamehub");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.Run();
