using FirstMud.Application;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using FirstMud.Infrastructure;
using FirstMud.Infrastructure.Data;
using FirstMud.Infrastructure.Neo4j;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);
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

// Neo4j driver + quest graph repository + lore seeder
builder.Services.AddNeo4j(builder.Configuration);

// Application layer services
builder.Services.AddApplicationServices();

// Game services
builder.Services.AddSingleton<AiPlayerService>();
builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());
builder.Services.AddScoped<CommandDispatcher>();
builder.Services.AddScoped<WorldStateService>();
builder.Services.AddScoped<GameNotificationService>();
builder.Services.AddScoped<StartupSeeder>();

var app = builder.Build();

// Auto-migrate database and seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    var maxRetries = 10;
    for (var i = 0; i < maxRetries; i++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (Exception ex) when (i < maxRetries - 1)
        {
            app.Logger.LogWarning("DB not ready (attempt {i}/{max}): {msg}", i + 1, maxRetries, ex.Message);
            await Task.Delay(3000);
        }
    }

    var seeder = scope.ServiceProvider.GetRequiredService<StartupSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

// Trigger AiPlayerService initialization (singleton — ensures it's created on startup)
app.Services.GetRequiredService<AiPlayerService>();

app.UseCors("GameClient");
app.MapHub<GameHub>("/gamehub");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapGet("/api/players", async (GameDbContext db) =>
{
    var players = await db.Players.Select(p => new { p.Id, p.Name, p.Level }).ToListAsync();
    return Results.Ok(players);
});

app.MapPost("/api/players", async (CreatePlayerRequest req, GameDbContext db) =>
{
    var player = Player.Create(req.Name, req.CraftingSeed ?? Random.Shared.Next());
    await db.Players.AddAsync(player);
    await db.SaveChangesAsync();
    return Results.Created($"/api/players/{player.Id}", new { player.Id, player.Name });
});

app.Run();

record CreatePlayerRequest(string Name, int? CraftingSeed);
