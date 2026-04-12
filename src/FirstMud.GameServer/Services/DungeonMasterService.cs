using System.Collections.Concurrent;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.GameServer.Hubs;
using FirstMud.Infrastructure.Data;
using FirstMud.Infrastructure.Neo4j;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Procedural content generation background service.
/// Runs independently of the main game loop and populates the world with
/// world events, wandering NPCs, quests, and new zones — all algorithmically,
/// with no external API calls.
/// </summary>
public class DungeonMasterService : BackgroundService
{
    // -----------------------------------------------------------------------
    // Tick schedule constants
    // -----------------------------------------------------------------------

    private const int WorldEventIntervalSeconds     = 60;        // 1 minute
    private const int WanderingNpcIntervalSeconds   = 300;       // 5 minutes
    private const int NewQuestIntervalSeconds       = 900;       // 15 minutes
    private const int NewZoneIntervalSeconds        = 1800;      // 30 minutes
    private const int NpcDespawnIntervalSeconds     = 600;       // 10 minutes (NPC lifespan)
    private const int LoopDelayMs                   = 1000;      // Heartbeat: 1 second

    // -----------------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------------

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<DungeonMasterService> _logger;
    private readonly Random _rng = new();

    // -----------------------------------------------------------------------
    // In-memory NPC registry
    // -----------------------------------------------------------------------

    private readonly ConcurrentDictionary<Guid, WanderingNpc> _activeNpcs = new();

    // -----------------------------------------------------------------------
    // Elapsed-time accumulators (seconds)
    // -----------------------------------------------------------------------

    private double _worldEventAccum;
    private double _npcAccum;
    private double _questAccum;
    private double _zoneAccum;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public DungeonMasterService(
        IServiceScopeFactory scopeFactory,
        IHubContext<GameHub> hub,
        ILogger<DungeonMasterService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub          = hub;
        _logger       = logger;
    }

    // -----------------------------------------------------------------------
    // Public accessors (for tests / future hub queries)
    // -----------------------------------------------------------------------

    public IReadOnlyDictionary<Guid, WanderingNpc> ActiveNpcs => _activeNpcs;

    // -----------------------------------------------------------------------
    // BackgroundService entry point
    // -----------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DungeonMasterService starting.");

        // Stagger the first ticks so they don't all fire at t=0 on startup
        _worldEventAccum = WorldEventIntervalSeconds * 0.3;
        _npcAccum        = WanderingNpcIntervalSeconds * 0.5;
        _questAccum      = NewQuestIntervalSeconds * 0.7;
        _zoneAccum       = NewZoneIntervalSeconds * 0.9;

        var lastTick = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LoopDelayMs, stoppingToken);

                var now     = DateTimeOffset.UtcNow;
                var elapsed = (now - lastTick).TotalSeconds;
                lastTick    = now;

                _worldEventAccum += elapsed;
                _npcAccum        += elapsed;
                _questAccum      += elapsed;
                _zoneAccum       += elapsed;

                // Despawn expired NPCs every loop iteration (cheap)
                DespawnExpiredNpcs(stoppingToken);

                if (_worldEventAccum >= WorldEventIntervalSeconds)
                {
                    _worldEventAccum -= WorldEventIntervalSeconds;
                    await FireWorldEventAsync(stoppingToken);
                }

                if (_npcAccum >= WanderingNpcIntervalSeconds)
                {
                    _npcAccum -= WanderingNpcIntervalSeconds;
                    await SpawnWanderingNpcAsync(stoppingToken);
                }

                if (_questAccum >= NewQuestIntervalSeconds)
                {
                    _questAccum -= NewQuestIntervalSeconds;
                    await GenerateQuestAsync(stoppingToken);
                }

                if (_zoneAccum >= NewZoneIntervalSeconds)
                {
                    _zoneAccum -= NewZoneIntervalSeconds;
                    await GenerateZoneAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DungeonMasterService: unhandled exception in main loop.");
            }
        }

        _logger.LogInformation("DungeonMasterService stopped.");
    }

    // =======================================================================
    // 1. WORLD EVENTS
    // =======================================================================

    private async Task FireWorldEventAsync(CancellationToken ct)
    {
        try
        {
            var (category, text) = BuildWorldEventText();

            _logger.LogDebug("DM WorldEvent [{Category}]: {Text}", category, text);

            await _hub.Clients.All.SendAsync("WorldEvent", new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                category,
                text
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DungeonMasterService: error in FireWorldEventAsync.");
        }
    }

    private (string Category, string Text) BuildWorldEventText()
    {
        // Pick a category weighted toward the atmospheric ones
        var categoryRoll = _rng.Next(100);
        var (category, templates) = categoryRoll switch
        {
            < 20 => ("weather",  WeatherTemplates),
            < 35 => ("wyrd",     WeaveTemplates),
            < 52 => ("faction",  FactionTemplates),
            < 65 => ("wildlife", WildlifeTemplates),
            < 80 => ("mystery",  MysteryTemplates),
            _    => ("rumor",    RumorTemplates),
        };

        var template = Pick(templates);
        var text     = FillTemplate(template);
        return (category, text);
    }

    private string FillTemplate(string template)
    {
        var result = template;

        result = result.Replace("{zone}",         Pick(ZoneNames));
        result = result.Replace("{world}",        "Aeldran");
        result = result.Replace("{faction}",      Pick(FactionNames));
        result = result.Replace("{monster_type}", Pick(MonsterNames));
        result = result.Replace("{monster}",      Pick(MonsterNames));
        result = result.Replace("{item}",         Pick(DialogueItems));
        result = result.Replace("{npc_name}",     BuildNpcName());
        result = result.Replace("{resource}",     Pick(ResourceNames));
        result = result.Replace("{element}",      Pick(ElementNames));
        result = result.Replace("{region}",       Pick(RegionNames));

        return result;
    }

    // =======================================================================
    // 2. WANDERING NPCs
    // =======================================================================

    private async Task SpawnWanderingNpcAsync(CancellationToken ct)
    {
        try
        {
            var id       = Guid.NewGuid();
            var name     = BuildNpcName();
            var role     = Pick(NpcRoles);
            var (x, y)   = PickNpcPosition();
            var dialogue = BuildDialogue(role);

            var npc = new WanderingNpc(id, name, role, x, y, dialogue, DateTimeOffset.UtcNow);
            _activeNpcs[id] = npc;

            _logger.LogDebug("DM NPC spawned: {Name} ({Role}) at ({X},{Y})", name, role, x, y);

            await _hub.Clients.All.SendAsync("NpcAppeared", new
            {
                id       = id.ToString(),
                name,
                role,
                x,
                y,
                dialogue
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DungeonMasterService: error in SpawnWanderingNpcAsync.");
        }
    }

    private (int X, int Y) PickNpcPosition()
    {
        // Place near one of the known zone anchors, with a small random offset
        var anchors = new[]
        {
            (8, 3), (13, 5), (24, 13), (28, 9), (32, 15),
            (34, 4), (20, 10), (26, 7), (36, 18)
        };
        var (ax, ay) = anchors[_rng.Next(anchors.Length)];
        var x = Math.Clamp(ax + _rng.Next(-3, 4), 0, ZoneGridLayout.GridWidth  - 1);
        var y = Math.Clamp(ay + _rng.Next(-3, 4), 0, ZoneGridLayout.GridHeight - 1);
        return (x, y);
    }

    private string BuildDialogue(string role)
    {
        var template = role switch
        {
            "Merchant"  => Pick(MerchantDialogues),
            "Wanderer"  => Pick(WandererDialogues),
            "Scout"     => Pick(ScoutDialogues),
            "Hermit"    => Pick(HermitDialogues),
            "Refugee"   => Pick(RefugeeDialogues),
            "Bard"      => Pick(BardDialogues),
            _           => Pick(WandererDialogues),
        };
        return FillTemplate(template);
    }

    private void DespawnExpiredNpcs(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-NpcDespawnIntervalSeconds);
        foreach (var (id, npc) in _activeNpcs)
        {
            if (npc.SpawnedAt > cutoff) continue;
            if (!_activeNpcs.TryRemove(id, out _)) continue;

            _logger.LogDebug("DM NPC despawned: {Name} ({Id})", npc.Name, id);

            // Fire-and-forget — we're in a sync context here
            _ = _hub.Clients.All.SendAsync("NpcDespawned", new { id = id.ToString() }, ct);
        }
    }

    // =======================================================================
    // 3. QUEST GENERATION
    // =======================================================================

    private async Task GenerateQuestAsync(CancellationToken ct)
    {
        try
        {
            var questId    = $"DM_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{_rng.Next(1000)}";
            var factionId  = PickFactionId();
            var difficulty = _rng.Next(1, 6); // 1-5
            var repReward  = 50 + difficulty * 90; // 140 – 500

            var (title, description, outcomes) = BuildQuestContent(difficulty);

            _logger.LogDebug("DM Quest generated: {Id} '{Title}' for {Faction}", questId, title, factionId);

            await using var scope  = _scopeFactory.CreateAsyncScope();
            var neo4jWrapper       = scope.ServiceProvider.GetRequiredService<Neo4jDriverWrapper>();

            await neo4jWrapper.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(
                    """
                    MERGE (q:Quest {questId: $questId})
                    SET q.title            = $title,
                        q.description      = $description,
                        q.factionId        = $factionId,
                        q.requiredTier     = $requiredTier,
                        q.requiredWorld    = $requiredWorld,
                        q.reputationReward = $reputationReward,
                        q.possibleOutcomes = $possibleOutcomes,
                        q.isWyrdQuest      = $isWyrdQuest,
                        q.isDmGenerated    = true,
                        q.generatedAt      = $generatedAt
                    """,
                    new
                    {
                        questId,
                        title,
                        description,
                        factionId        = (int)factionId,
                        requiredTier     = 0, // Unknown — anyone can pick it up
                        requiredWorld    = (int)WorldId.Aeldran,
                        reputationReward = repReward,
                        possibleOutcomes = outcomes,
                        isWyrdQuest      = false,
                        generatedAt      = DateTimeOffset.UtcNow.ToString("O"),
                    });
            }, ct);

            await _hub.Clients.All.SendAsync("NewQuestAvailable", new
            {
                questId,
                title,
                faction   = factionId.ToString(),
                repReward,
                difficulty,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DungeonMasterService: error in GenerateQuestAsync.");
        }
    }

    private (string Title, string Description, string[] Outcomes) BuildQuestContent(int difficulty)
    {
        var templateIndex = _rng.Next(QuestTitleTemplates.Length);
        var titleTemplate = QuestTitleTemplates[templateIndex];
        var descTemplate  = QuestDescTemplates[templateIndex % QuestDescTemplates.Length];

        var adjective = Pick(QuestAdjectives);
        var noun      = Pick(QuestNouns);
        var zone      = Pick(ZoneNames);
        var monster   = Pick(MonsterNames);
        var item      = Pick(DialogueItems);
        var npcName   = BuildNpcName();
        var amount    = _rng.Next(3, 12);
        var resource  = Pick(ResourceNames);
        var role      = Pick(NpcRoles);

        string title = titleTemplate
            .Replace("{adjective}",  adjective)
            .Replace("{noun}",       noun)
            .Replace("{zone}",       zone)
            .Replace("{monster}",    monster)
            .Replace("{item}",       item)
            .Replace("{npc_name}",   npcName)
            .Replace("{amount}",     amount.ToString())
            .Replace("{resource}",   resource)
            .Replace("{role}",       role);

        string description = descTemplate
            .Replace("{adjective}",  adjective)
            .Replace("{noun}",       noun)
            .Replace("{zone}",       zone)
            .Replace("{monster}",    monster)
            .Replace("{item}",       item)
            .Replace("{npc_name}",   npcName)
            .Replace("{amount}",     amount.ToString())
            .Replace("{resource}",   resource)
            .Replace("{role}",       role);

        var successFlavors = new[]
        {
            "completed", "succeeded", "resolved", "accomplished", "finished"
        };
        var failFlavors = new[]
        {
            "failed", "abandoned", "retreated", "delayed"
        };

        var outcomes = new[]
        {
            Pick(successFlavors),
            Pick(failFlavors),
        };

        return (title, description, outcomes);
    }

    private FactionId PickFactionId()
    {
        var ids = Enum.GetValues<FactionId>();
        return ids[_rng.Next(ids.Length)];
    }

    // =======================================================================
    // 4. ZONE GENERATION
    // =======================================================================

    private async Task GenerateZoneAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db                = scope.ServiceProvider.GetRequiredService<GameDbContext>();

            // Find the next available ZoneId for Aeldran
            var existingZoneIds = db.Zones
                .Where(z => z.WorldId == WorldId.Aeldran)
                .Select(z => z.ZoneId)
                .ToHashSet();

            var nextZoneId = 10; // Seeded zones use 1-9
            while (existingZoneIds.Contains(nextZoneId))
                nextZoneId++;

            var (name, description, symbol, dangerLevel) = BuildZoneContent(nextZoneId);

            _logger.LogDebug("DM Zone generated: #{Id} '{Name}' (Danger {Danger})", nextZoneId, name, dangerLevel);

            var zone = Zone.Create(
                WorldId.Aeldran,
                nextZoneId,
                name,
                description,
                symbol,
                dangerLevel);

            db.Zones.Add(zone);

            // Seed 2 resource nodes for the new zone
            var (res1, res2) = PickResourcePair(dangerLevel);
            var node1 = ResourceNode.Create(zone.Id, res1, maxYield: 20, regenerationRate: 2);
            var node2 = ResourceNode.Create(zone.Id, res2, maxYield: 15, regenerationRate: 1);
            db.ResourceNodes.Add(node1);
            db.ResourceNodes.Add(node2);

            await db.SaveChangesAsync(ct);

            // Compute grid position for the client
            var (posX, posY) = ZoneGridLayout.GetPosition(zone.Id);

            await _hub.Clients.All.SendAsync("NewZoneDiscovered", new
            {
                zoneId      = zone.Id.ToString(),
                zoneNumber  = nextZoneId,
                name,
                description,
                asciiSymbol = symbol,
                dangerLevel,
                x           = posX,
                y           = posY,
                worldId     = WorldId.Aeldran.ToString(),
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "DungeonMasterService: error in GenerateZoneAsync.");
        }
    }

    private (string Name, string Description, string Symbol, int DangerLevel)
        BuildZoneContent(int zoneNumber)
    {
        // Danger level scales loosely with zone number (later = further out = more dangerous)
        var dangerLevel = Math.Clamp(1 + (zoneNumber - 10) / 3 + _rng.Next(1, 4), 1, 10);

        var nameStyle = _rng.Next(3);
        string name = nameStyle switch
        {
            0 => $"{Pick(ZoneAdjectives)} {Pick(ZoneNouns)}",
            1 => $"The {Pick(ZoneNouns)} of {Pick(NamedPlaces)}",
            _ => $"{Pick(ZoneAdjectives)} {Pick(NamedPlaces)}'s {Pick(ZoneNouns)}",
        };

        var descTemplate = Pick(ZoneDescTemplates);
        var description  = FillTemplate(descTemplate)
            .Replace("{zone_name}",   name)
            .Replace("{danger_adj}",  dangerLevel > 6 ? "deadly" : dangerLevel > 3 ? "dangerous" : "quiet");

        var symbolPool = dangerLevel > 6
            ? new[] { "*", "X", "!", "%" }
            : dangerLevel > 3
                ? new[] { "^", "#", "~", "+" }
                : new[] { ".", ",", "-", "_" };
        var symbol = Pick(symbolPool);

        return (name, description, symbol, dangerLevel);
    }

    private (ResourceType R1, ResourceType R2) PickResourcePair(int dangerLevel)
    {
        var all = Enum.GetValues<ResourceType>();
        var r1  = all[_rng.Next(all.Length)];
        var r2  = all[_rng.Next(all.Length)];
        // High-danger zones bias toward Metal
        if (dangerLevel >= 7 && _rng.Next(2) == 0)
            r1 = ResourceType.Metal;
        return (r1, r2);
    }

    // =======================================================================
    // Utility helpers
    // =======================================================================

    private T Pick<T>(T[] pool) => pool[_rng.Next(pool.Length)];

    private string BuildNpcName()
    {
        var syllableCount = _rng.Next(2, 4); // 2 or 3
        var name = string.Empty;
        for (var i = 0; i < syllableCount; i++)
            name += Pick(NameSyllables);
        // Capitalise the first letter
        return char.ToUpper(name[0]) + name[1..];
    }

    // =======================================================================
    // WORD / NAME POOLS
    // =======================================================================

    // --- Zone names used in templates ---
    private static readonly string[] ZoneNames =
    [
        "the Caervorn Highlands", "the Thornwood", "Portmere", "the Gravenmarsh",
        "the Drowned Coast", "the Ashen Reach", "the Starting Road", "Gravenhold",
        "the Maw Borderlands", "Coldmere", "Ironspire Ridge", "the Rootweave",
        "the Tidegate", "the Pale City", "the Sundering Scar", "Ashcross",
        "Veldann", "the Golvari Marches", "the Wyrd-Paths crossing", "the Deeps threshold",
    ];

    // --- Region names ---
    private static readonly string[] RegionNames =
    [
        "the Highlands", "the Thornwood", "the Marches", "the Drowned Coast",
        "the Ashen Reach", "the Gravenmarsh", "the southern coast", "the deep Deeps",
    ];

    // --- Faction names ---
    private static readonly string[] FactionNames =
    [
        "House Caervorn", "Thornwood Coven", "Emerald Compact", "Gravenguard",
        "Fairgean", "Golvari", "Ashen Court",
    ];

    // --- Monster names ---
    private static readonly string[] MonsterNames =
    [
        "Cave Rat", "Fire Imp", "Bog Wraith", "Thornweaver", "Ash Golem",
        "Tide Lurker", "Wind Phantom", "Stone Sentinel", "Shadow Cat", "Wyrd Hound",
        "Moor Stalker", "Frost Spider", "Ember Lizard", "Grave Beetle", "Deepwater Eel",
        "Rootweave Horror", "Ashen Revenant", "Dravenite Golem", "Pale Shade", "Marsh Crawler",
    ];

    // --- NPC name syllables ---
    private static readonly string[] NameSyllables =
    [
        "kor", "dren", "ael", "thi", "mar", "vos", "eld", "wyn", "ash", "bel",
        "gar", "fen", "ith", "ran", "sol", "mira", "val", "tor", "ken", "daw",
        "vex", "drest", "sor", "lys", "bran", "cal", "mael", "vor", "wyn", "edd",
    ];

    // --- NPC roles ---
    private static readonly string[] NpcRoles =
    [
        "Merchant", "Wanderer", "Scout", "Hermit", "Refugee", "Bard",
    ];

    // --- Items for dialogue ---
    private static readonly string[] DialogueItems =
    [
        "Dravenite dust", "healing herbs", "iron ore", "leather strips", "beast hides",
        "Thornwood resin", "Compact spice", "war-steel shards", "wyrd-thread", "coven salve",
        "Ardweld fragments", "deep coral", "tidal iron", "deepstone chips", "focus stones",
    ];

    // --- Resources ---
    private static readonly string[] ResourceNames =
    [
        "iron ore", "timber", "healing herbs", "beast hides", "dravenite dust",
        "Thornwood resin", "coven-blessed water", "moonstone fragments",
        "deep crystal", "kelp silk", "fungal spore",
    ];

    // --- Elements ---
    private static readonly string[] ElementNames =
    [
        "Fire", "Water", "Earth", "Air", "Aether",
    ];

    // --- Quest adjectives ---
    private static readonly string[] QuestAdjectives =
    [
        "forgotten", "cursed", "ancient", "stolen", "corrupted", "burning", "frozen",
        "sundered", "whispered", "shattered", "buried", "luminous", "hollow", "pale",
        "wandering",
    ];

    // --- Quest nouns ---
    private static readonly string[] QuestNouns =
    [
        "relic", "codex", "seal", "beacon", "cache", "archive", "shrine", "ward",
        "fragment", "vessel", "grimoire", "pendant", "key", "token", "sigil",
    ];

    // --- Zone adjectives ---
    private static readonly string[] ZoneAdjectives =
    [
        "Frozen", "Burning", "Ancient", "Forgotten", "Hidden", "Cursed", "Sacred",
        "Shattered", "Sunken", "Pale", "Drowned", "Wyrd-Touched", "Hollow", "Verdant",
        "Storm-Kissed", "Ashen", "Ironbound", "Thornfast", "Deepsealed", "Moonlit",
        "Tide-Scarred", "Root-Woven", "Ember-lit", "Gale-Swept",
    ];

    // --- Zone nouns ---
    private static readonly string[] ZoneNouns =
    [
        "Hollow", "Ruins", "Caverns", "Crossing", "Glade", "Bastion", "Spire",
        "Depths", "Vale", "Reach", "Crags", "Warrens", "Mire", "Bluffs", "Shore",
        "Barrow", "Fastness", "Passage", "Grove", "Marshes", "Expanse", "Pinnacle",
        "Gap", "Narrows",
    ];

    // --- Named places for zone titles ---
    private static readonly string[] NamedPlaces =
    [
        "Aldric", "Vexar", "Morvaine", "Kelthis", "Draeven", "Mira", "Drest",
        "Vorath", "Ashvale", "Caervorn", "Fairgean", "Golvari", "Thornweld",
        "Sundering", "Ardweld",
    ];

    // -----------------------------------------------------------------------
    // World event templates (55+ total)
    // -----------------------------------------------------------------------

    private static readonly string[] WeatherTemplates =
    [
        "A cold wind sweeps down from {zone}.",
        "Rain begins to fall across {world}.",
        "The sky above {zone} turns an unsettling shade of grey.",
        "Hail patters against stone rooftops near {zone}.",
        "A warm southern breeze carries the scent of salt from {zone}.",
        "Fog rolls in thick from the direction of {zone}.",
        "The stars above {zone} are swallowed by cloud.",
        "A dry crackling in the air presages lightning near {zone}.",
        "Snow dusts the high ground around {zone} for the first time this season.",
        "The wind shifts. Something in {zone} smells of old ash.",
    ];

    private static readonly string[] WeaveTemplates =
    [
        "The Weave shudders. Something stirs in {zone}.",
        "Threads of raw magic drift through the air near {zone}.",
        "A mage in {zone} Works something large — you can feel the displacement.",
        "The Rootweave hums louder than usual. Coven activity in {zone}.",
        "Your brooch grows warm. The Weave is active near {zone}.",
        "An Aether disturbance ripples outward from {zone}. Something important happened.",
        "The ambient Weave thickens around {zone}. Dravenite is near.",
        "A pulse of {element} energy passes through the region. Someone is practicing.",
        "The Weave tastes of iron near {zone}. Something was unmade recently.",
        "Wildfolk scatter from {zone}. They sense something the Working cannot name.",
        "The Rootweave is silent for three heartbeats. Then it resumes, changed.",
    ];

    private static readonly string[] FactionTemplates =
    [
        "A {faction} patrol passes through {zone}.",
        "{faction} emissaries have been spotted near {zone}.",
        "Word comes from {zone}: {faction} has closed the road.",
        "Riders carrying {faction} colors were seen departing {zone} at speed.",
        "A {faction} banner has been raised at {zone}. No one is sure what it means.",
        "{faction} soldiers are questioning travelers at {zone}.",
        "A {faction} merchant caravan was raided near {zone}. Survivors are silent.",
        "The {faction} has posted a bounty — someone stirred trouble in {zone}.",
        "An emissary from {faction} was turned away at {zone}.",
        "Rumors of {faction} spies operating through {zone} have surfaced again.",
    ];

    private static readonly string[] WildlifeTemplates =
    [
        "A flock of ravens circles above {zone}.",
        "Wolves howl somewhere beyond {zone}.",
        "A {monster_type} has been reported near {zone} — travelers are warned.",
        "An unusual migration of birds passes over {zone}, heading inland.",
        "Hoof-prints the size of shields were found near {zone}. No one knows the beast.",
        "Something large moved through {zone} last night. The trees remember it.",
        "The fish in the streams near {zone} are running upstream. Out of season.",
        "A white stag was glimpsed at dusk near {zone}. The old folk say it is an omen.",
        "Insect-swarms thicker than smoke have descended on {zone}.",
        "A pack of Wyrd Hounds was heard baying in {zone} before dawn.",
    ];

    private static readonly string[] MysteryTemplates =
    [
        "A strange light appears on the horizon near {zone}.",
        "The ground trembles faintly in the direction of {zone}.",
        "Three travelers vanished on the road through {zone}. Their horses returned alone.",
        "A figure in grey was seen standing at the edge of {zone} since last night. Still there now.",
        "The bells in {zone} rang at midnight. There are no bells in {zone}.",
        "Something burned in {zone} last night. Nothing was missing in the morning.",
        "A child in {zone} began speaking in the Ardweld tongue. They have never studied it.",
        "The moon was doubled last night above {zone}. Only for a moment.",
        "Every clock in {zone} stopped at the same hour. None of them have restarted.",
        "The Ashen Court has been quiet too long. {zone} feels watched.",
    ];

    private static readonly string[] RumorTemplates =
    [
        "Travelers speak of {monster_type} sightings near {zone}.",
        "A merchant mentions a hidden cache in {zone} — for the right buyer.",
        "Someone in {zone} is paying well for {item}. No questions asked.",
        "A Gravenguard scout returned from {zone} and will not say what they found.",
        "Whispers from {zone}: a {monster_type} nest has formed under the old road.",
        "An innkeeper in {zone} claims a {faction} agent left a sealed letter. Uncollected.",
        "Word from {zone}: the road south is watched. Travel carefully.",
        "A cartographer has updated the maps of {zone}. The changes are… unexpected.",
        "Someone is buying {item} in bulk near {zone}. The Compact is curious.",
        "A Coven mage returned from {zone} with silver streaks in her hair. She is not talking.",
        "The Gravenguard are excavating something in {zone}. Officially it is 'surveying'.",
        "Traders from {zone} say a new figure has been seen near the old Ardweld ruins.",
    ];

    // -----------------------------------------------------------------------
    // NPC dialogue templates
    // -----------------------------------------------------------------------

    private static readonly string[] MerchantDialogues =
    [
        "I have wares, if you have coin. {item} for sale, fresh from {zone}.",
        "Trade well, Rider. I've got {item} — best price this side of {zone}.",
        "The roads from {zone} were rough but the goods survived. Interested?",
        "Stock's running low. The trouble near {zone} dried up the supply of {item}.",
    ];

    private static readonly string[] WandererDialogues =
    [
        "I've been walking since {zone}. The roads aren't safe anymore.",
        "Don't linger near {zone}. I saw something I can't explain.",
        "Heading south. Everything I owned is in {zone} and I'm not going back.",
        "You're riding toward {zone}? I rode away from {zone}. Think on that.",
    ];

    private static readonly string[] ScoutDialogues =
    [
        "Be careful near {zone}. I saw {monster} tracks not far from here.",
        "The {faction} have moved east of {zone}. The road is clear — for now.",
        "Stay off the low path through {zone}. Something nests there.",
        "I've mapped three new {monster} warrens between here and {zone}.",
    ];

    private static readonly string[] HermitDialogues =
    [
        "The Weave was different before the Sundering. You can still taste it near {zone}.",
        "I left {zone} when the grey dreams started. That was twenty years ago.",
        "No one visits the old stones at {zone}. They should. And they shouldn't.",
        "The Rootweave heard something it won't repeat. Near {zone}.",
    ];

    private static readonly string[] RefugeeDialogues =
    [
        "The {faction} came to {zone}. We had two days to leave.",
        "My family is still in {zone}. I'm going back when it's safer. When.",
        "I had a home in {zone}. A proper one. Now I carry what I can.",
        "Don't trust the {faction} promises near {zone}. I believed them once.",
    ];

    private static readonly string[] BardDialogues =
    [
        "Let me tell you the tale of {npc_name}, who rode from {zone} and never returned.",
        "I heard a song in {zone} that I've never heard before. Old words. Older than the Covens.",
        "The ballad of the fall of {zone} has seventeen verses now. I add one each year.",
        "Songs remember what commanders bury. Ask me about {zone}.",
    ];

    // -----------------------------------------------------------------------
    // Quest templates
    // -----------------------------------------------------------------------

    private static readonly string[] QuestTitleTemplates =
    [
        "Investigate the {adjective} {noun} near {zone}",
        "Defeat the {monster} terrorizing {zone}",
        "Deliver {item} to {npc_name} in {zone}",
        "Gather {amount} units of {resource} from {zone}",
        "Escort a {role} safely through {zone}",
        "Retrieve the {adjective} {noun} from {zone}",
        "Negotiate with the {monster} at {zone}",
        "Uncover the truth about {zone}",
        "Protect {npc_name} at {zone}",
        "Clear the {adjective} {noun} blocking the road through {zone}",
    ];

    private static readonly string[] QuestDescTemplates =
    [
        "Something {adjective} has surfaced near {zone}. Locals are uneasy. " +
            "Investigate what the {noun} is and who left it there.",
        "A {monster} has been preying on travelers near {zone}. " +
            "The locals can't handle it. You've been asked to deal with it — permanently.",
        "{npc_name} in {zone} sent word: they need {item} delivered before nightfall. " +
            "The roads between here and {zone} are not empty.",
        "The {zone} region yields {resource} that isn't found elsewhere. Gather {amount} units. " +
            "The area is not uncontested.",
        "A {role} needs to reach {zone} and cannot travel alone. " +
            "What threatens the road is not only bandits.",
        "The {adjective} {noun} was reported missing from {zone} three days ago. " +
            "Someone took it deliberately. Find it before it leaves the region.",
        "The {monster} near {zone} haven't attacked in two days — unusual. " +
            "Find out why before the peace ends badly.",
        "Stories about {zone} don't match the maps. Someone is hiding something. " +
            "Go and find out what.",
        "{npc_name} has information the wrong people want. Keep them safe at {zone} " +
            "until they can be moved.",
        "A {adjective} {noun} has blocked the main road through {zone}. " +
            "Trade has stopped. Clear it.",
    ];

    // -----------------------------------------------------------------------
    // Zone description templates
    // -----------------------------------------------------------------------

    private static readonly string[] ZoneDescTemplates =
    [
        "A {danger_adj} expanse of broken land where the Weave runs thin and strange. " +
            "Explorers report signs of Ardweld stonework beneath the surface.",
        "Wild country, largely unmapped. The locals near {zone} say nothing good comes from here — " +
            "but they still send their scouts.",
        "The Weave is thick here, almost visible. Whatever happened in this region during the Sundering " +
            "left a mark that has not faded.",
        "High ground above {zone}, swept by cold winds and watched by something that has no name " +
            "in the current tongue.",
        "A crossing point between settled land and something older. The {element} element is strong here.",
        "Deep country where roads thin to tracks and tracks thin to nothing. " +
            "Resource-rich, if you can survive the extraction.",
        "A region that appears on no official Compact map. It exists anyway.",
        "Ruins of something that predates House Caervorn, the Covens, and possibly the Ardweld. " +
            "The Gravenguard has noted it. They haven't visited.",
        "The border between Aeldran's surface world and whatever lies beneath grows porous here. " +
            "Golvari scouts have been seen, watching.",
        "Where the Fairgean tides reach inland and do not fully retreat. " +
            "Strange things wash up. Stranger things come looking for them.",
    ];
}

// ---------------------------------------------------------------------------
// WanderingNpc record
// ---------------------------------------------------------------------------

/// <summary>
/// Represents a procedurally generated NPC currently active on the world map.
/// Stored in-memory only — despawns after <see cref="DungeonMasterService.NpcDespawnIntervalSeconds"/> seconds.
/// </summary>
public sealed record WanderingNpc(
    Guid Id,
    string Name,
    string Role,
    int X,
    int Y,
    string Dialogue,
    DateTimeOffset SpawnedAt);
