using FirstMud.Application;
using FirstMud.Application.Events;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Engine.Commands;
using FirstMud.Engine.DependencyInjection;
using FirstMud.Engine.Events;
using FirstMud.Engine.Messaging;
using FirstMud.Engine.Tick;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Handlers;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Hubs.Parsers;
using FirstMud.GameServer.Services;
using FirstMud.GameServer.Services.EventOrchestrators;
using FirstMud.GameServer.Services.Snapshots;
using FirstMud.GameServer.TickHandlers;
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

// Engine: high-level composition. IGameNotifier is registered below by the host
// because it needs SignalR (which the Engine project doesn't reference).
builder.Services
    .AddEngineMessaging()
    .AddEngineEvents()
    .AddEngineCommandPipeline()
    .AddEngineTickLoop();

// Game services
builder.Services.AddSingleton<AiPlayerService>();

builder.Services.AddSingleton<DungeonMasterService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DungeonMasterService>());
builder.Services.AddScoped<WorldStateService>();
builder.Services.AddScoped<GameNotificationService>();
// Expose the SignalR-backed notifier under the engine's abstraction so the
// generic GameLoopService can broadcast command errors without knowing about
// IHubContext<GameHub>.
builder.Services.AddScoped<IGameNotifier>(sp => sp.GetRequiredService<GameNotificationService>());
builder.Services.AddScoped<StartupSeeder>();

// Snapshot services — single source of truth for building Inventory/Storage/City/Companion payloads
builder.Services.AddScoped<InventorySnapshotService>();
builder.Services.AddScoped<StorageSnapshotService>();
builder.Services.AddScoped<CitySnapshotService>();
builder.Services.AddScoped<CompanionListSnapshotService>();

// Event bus orchestrators — subscribe to integration events and broadcast refreshed snapshots
builder.Services.AddScoped<InventoryRefreshOrchestrator>();
builder.Services.AddScoped<StorageRefreshOrchestrator>();
builder.Services.AddScoped<CompanionListOrchestrator>();
builder.Services.AddScoped<CityRefreshOrchestrator>();
builder.Services.AddScoped<IGameEventSubscriber<ItemConsumedEvent>>(sp => sp.GetRequiredService<InventoryRefreshOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<ItemAddedToInventoryEvent>>(sp => sp.GetRequiredService<InventoryRefreshOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<EquipmentChangedEvent>>(sp => sp.GetRequiredService<InventoryRefreshOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<ItemConsumedEvent>>(sp => sp.GetRequiredService<StorageRefreshOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<StorageChangedEvent>>(sp => sp.GetRequiredService<StorageRefreshOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<CompanionStateChangedEvent>>(sp => sp.GetRequiredService<CompanionListOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<CompanionStateChangedEvent>>(sp => sp.GetRequiredService<CityRefreshOrchestrator>());
builder.Services.AddScoped<IGameEventSubscriber<CityStateChangedEvent>>(sp => sp.GetRequiredService<CityRefreshOrchestrator>());

// Tick handlers — each is one periodic job the loop fires on its own cadence.
builder.Services.AddScoped<ITickHandler, AiPlayerTickHandler>();
builder.Services.AddScoped<ITickHandler, AutomationSweepTickHandler>();
builder.Services.AddScoped<ITickHandler, HomesteadHealTickHandler>();
builder.Services.AddScoped<ITickHandler, WeaveRegenTickHandler>();
builder.Services.AddScoped<ITickHandler, HomesteadCompanionTickHandler>();
builder.Services.AddScoped<ITickHandler, CompanionDriftTickHandler>();
builder.Services.AddScoped<ITickHandler, BuildingConstructionTickHandler>();
builder.Services.AddScoped<ITickHandler, ResourceRegenTickHandler>();

// Shared combat utilities and farming orchestrator
builder.Services.AddScoped<CombatHelpers>();
builder.Services.AddScoped<FarmingOrchestrator>();
builder.Services.AddSingleton<InventoryDepositService>();

// Quest objective interaction
builder.Services.AddSingleton<QuestProgressTracker>();
builder.Services.AddScoped<QuestAutoCompleteService>();

// Command handlers — one per command type (ICommandHandler<TCommand>)
builder.Services.AddScoped<ICommandHandler<MoveCommand>, MoveCommandHandler>();
builder.Services.AddScoped<ICommandHandler<InteractCommand>, InteractCommandHandler>();
builder.Services.AddScoped<ICommandHandler<OpenInventoryCommand>, OpenInventoryCommandHandler>();
builder.Services.AddScoped<ICommandHandler<EquipCommand>, EquipCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UnequipCommand>, UnequipCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AcceptQuestCommand>, AcceptQuestCommandHandler>();
builder.Services.AddScoped<ICommandHandler<CompleteQuestCommand>, CompleteQuestCommandHandler>();
builder.Services.AddScoped<ICommandHandler<GetAvailableQuestsCommand>, GetAvailableQuestsCommandHandler>();
builder.Services.AddScoped<ICommandHandler<EnterZoneCommand>, EnterZoneCommandHandler>();
builder.Services.AddScoped<ICommandHandler<StartCombatCommand>, StartCombatCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UseCombatAbilityCommand>, UseCombatAbilityCommandHandler>();
builder.Services.AddScoped<ICommandHandler<FleeCombatCommand>, FleeCombatCommandHandler>();
builder.Services.AddScoped<ICommandHandler<PortalHomeCommand>, PortalHomeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<PortalBackCommand>, PortalBackCommandHandler>();
builder.Services.AddScoped<ICommandHandler<HarvestCommand>, HarvestCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DepositCommand>, DepositCommandHandler>();
builder.Services.AddScoped<ICommandHandler<WithdrawCommand>, WithdrawCommandHandler>();
builder.Services.AddScoped<ICommandHandler<OpenStorageCommand>, OpenStorageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ExpandStorageCommand>, ExpandStorageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AutoFarmCommand>, AutoFarmCommandHandler>();
builder.Services.AddScoped<ICommandHandler<SalvageCommand>, SalvageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<SalvageAllCommand>, SalvageAllCommandHandler>();
builder.Services.AddScoped<ICommandHandler<SetAutoSalvageCommand>, SetAutoSalvageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<LockItemCommand>, LockItemCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ViewCompanionsCommand>, ViewCompanionsCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ActivateCompanionCommand>, ActivateCompanionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeactivateCompanionCommand>, DeactivateCompanionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ImbueCommand>, ImbueCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AssignCompanionDutyCommand>, AssignCompanionDutyCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RecallCompanionCommand>, RecallCompanionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<QueueSalvageCommand>, QueueSalvageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<CraftCommand>, CraftCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ViewRecipesCommand>, ViewRecipesCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UseConsumableCommand>, UseConsumableCommandHandler>();
builder.Services.AddScoped<ICommandHandler<InteractQuestCommand>, InteractQuestCommandHandler>();
builder.Services.AddScoped<ICommandHandler<SmeltCommand>, SmeltCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ToggleCompanionAutoRotateCommand>, ToggleCompanionAutoRotateCommandHandler>();
builder.Services.AddScoped<ICommandHandler<PlaceBuildingCommand>, PlaceBuildingCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AssignBuilderCommand>, AssignBuilderCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UnassignBuilderCommand>, UnassignBuilderCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ViewCityCommand>, ViewCityCommandHandler>();
builder.Services.AddScoped<ICommandHandler<BuildStaffEverythingCommand>, BuildStaffEverythingCommandHandler>();
builder.Services.AddScoped<ICommandHandler<PracticeEnchantingCommand>, PracticeEnchantingCommandHandler>();

// SignalR command parsing: one ICommandParser per command name, plus the
// registry-based dispatcher. Add a new command = add one parser + one line here.
// (No Scrutor: explicit registrations keep the dep surface lean.)
builder.Services.AddSingleton<ICommandParser, MoveCommandParser>();
builder.Services.AddSingleton<ICommandParser, AttackCommandParser>();
builder.Services.AddSingleton<ICommandParser, UseSkillCommandParser>();
builder.Services.AddSingleton<ICommandParser, InteractCommandParser>();
builder.Services.AddSingleton<ICommandParser, PickupItemCommandParser>();
builder.Services.AddSingleton<ICommandParser, OpenInventoryCommandParser>();
builder.Services.AddSingleton<ICommandParser, CraftCommandParser>();
builder.Services.AddSingleton<ICommandParser, AcceptQuestCommandParser>();
builder.Services.AddSingleton<ICommandParser, CompleteQuestCommandParser>();
builder.Services.AddSingleton<ICommandParser, UsePortalCommandParser>();
builder.Services.AddSingleton<ICommandParser, ManageBaseAssetCommandParser>();
builder.Services.AddSingleton<ICommandParser, StartCombatCommandParser>();
builder.Services.AddSingleton<ICommandParser, UseCombatAbilityCommandParser>();
builder.Services.AddSingleton<ICommandParser, FleeCombatCommandParser>();
builder.Services.AddSingleton<ICommandParser, GetAvailableQuestsCommandParser>();
builder.Services.AddSingleton<ICommandParser, EnterZoneCommandParser>();
builder.Services.AddSingleton<ICommandParser, PortalHomeCommandParser>();
builder.Services.AddSingleton<ICommandParser, PortalBackCommandParser>();
builder.Services.AddSingleton<ICommandParser, HarvestCommandParser>();
builder.Services.AddSingleton<ICommandParser, DepositCommandParser>();
builder.Services.AddSingleton<ICommandParser, WithdrawCommandParser>();
builder.Services.AddSingleton<ICommandParser, OpenStorageCommandParser>();
builder.Services.AddSingleton<ICommandParser, ExpandStorageCommandParser>();
builder.Services.AddSingleton<ICommandParser, AutoFarmCommandParser>();
builder.Services.AddSingleton<ICommandParser, EquipCommandParser>();
builder.Services.AddSingleton<ICommandParser, UnequipCommandParser>();
builder.Services.AddSingleton<ICommandParser, SalvageCommandParser>();
builder.Services.AddSingleton<ICommandParser, SalvageAllCommandParser>();
builder.Services.AddSingleton<ICommandParser, SetAutoSalvageCommandParser>();
builder.Services.AddSingleton<ICommandParser, LockItemCommandParser>();
builder.Services.AddSingleton<ICommandParser, ViewCompanionsCommandParser>();
builder.Services.AddSingleton<ICommandParser, ActivateCompanionCommandParser>();
builder.Services.AddSingleton<ICommandParser, DeactivateCompanionCommandParser>();
builder.Services.AddSingleton<ICommandParser, ImbueCommandParser>();
builder.Services.AddSingleton<ICommandParser, AssignCompanionDutyCommandParser>();
builder.Services.AddSingleton<ICommandParser, RecallCompanionCommandParser>();
builder.Services.AddSingleton<ICommandParser, QueueSalvageCommandParser>();
builder.Services.AddSingleton<ICommandParser, ViewRecipesCommandParser>();
builder.Services.AddSingleton<ICommandParser, UseConsumableCommandParser>();
builder.Services.AddSingleton<ICommandParser, InteractQuestCommandParser>();
builder.Services.AddSingleton<ICommandParser, SmeltCommandParser>();
builder.Services.AddSingleton<ICommandParser, ToggleCompanionAutoRotateCommandParser>();
builder.Services.AddSingleton<ICommandParser, PlaceBuildingCommandParser>();
builder.Services.AddSingleton<ICommandParser, AssignBuilderCommandParser>();
builder.Services.AddSingleton<ICommandParser, UnassignBuilderCommandParser>();
builder.Services.AddSingleton<ICommandParser, ViewCityCommandParser>();
builder.Services.AddSingleton<ICommandParser, BuildStaffEverythingCommandParser>();
builder.Services.AddSingleton<ICommandParser, PracticeEnchantingCommandParser>();
builder.Services.AddSingleton<GameServerCommandFactory>();

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

// Dev-only: teleport a player back to the Starting Road. Used by
// Playwright tests (beforeEach) so gameplay state is deterministic.
app.MapPost("/api/players/{id:guid}/reset-position", async (Guid id, GameDbContext db) =>
{
    var player = await db.Players.FindAsync(id);
    if (player is null) return Results.NotFound();
    var (x, y) = FirstMud.GameServer.Services.ZoneGridLayout.StartingRoad;
    player.Move(new FirstMud.Domain.ValueObjects.Position(
        player.Position.World, player.Position.ZoneId, x, y));
    await db.SaveChangesAsync();
    return Results.Ok(new { player.Id, X = x, Y = y });
});

app.Run();

record CreatePlayerRequest(string Name, int? CraftingSeed);
