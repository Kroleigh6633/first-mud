using FirstMud.Application;
using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.DesignTools.Tools.ProgressionSim;

/// <summary>
/// Full-play progression simulator. Runs an N-hour session for a fresh
/// character under a named playstyle and emits per-hour snapshots plus a
/// full action log.
///
/// What's modelled "for real" (formulas match live where practical):
///   - Combat resolution via <see cref="CombatSimulationService"/> + danger
///     scaling via <see cref="MonsterScaling"/>.
///   - Player XP curve: Level = 1 + floor(sqrt(xp/100)) (mirrors Player.CalculateLevel).
///   - Combat XP per kill: 20 * level-diff multiplier (mirrors CombatHelpers).
///   - Companion layer thresholds: per-type tables from Companion.cs.
///   - Crafting skill from successful crafts; workmanship = avgInputs + skill/20.
///   - Loot drop rolls use dropChance.base + danger*perDangerLevel; biome pool
///     selected by zone biome; common-materials pool mixed in at configured %.
///
/// What we deliberately simplify (documented):
///   - Time: each hour ~= 12 "action slots" of 5 in-game minutes. Travel,
///     death downtime, inventory management are not modelled (noted as a
///     constant-factor fudge — real numbers will be 10-20% slower).
///   - Salvage: a craft failure / excess equipment is salvaged back to a
///     single material unit with 70% success (live formula uses per-slot
///     yields; close enough for economy-level conclusions).
///   - Buildings / auto-farm / hirelings / food economy: NOT modelled.
///     These are Phase-2 content and would dominate the sim if included.
///   - Enchanting / imbue: only tracks "enchanting-mat" balance as a
///     bottleneck metric — no imbue recipe resolution.
///   - Companion usage: each combat action credits 10 usage to each active
///     companion (live: per-ability, varies). Close enough for layer curves.
/// </summary>
public sealed class ProgressionSimulator
{
    public const int ActionsPerHour = 12;       // 5 in-game min per action
    public const int TimePerCombat  = 2;        // 2 action slots
    public const int TimePerHarvest = 1;
    public const int TimePerCraft   = 1;
    public const int TimePerSalvage = 1;

    /// <summary>Reagent + component item-names classified as enchanting materials for bottleneck reporting.</summary>
    public static readonly HashSet<string> EnchantingMats = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dravenite Dust", "Wyrd Shard", "Moonbloom Petal", "Fire Crystal",
        "Diamond Shard", "Pearl", "Marsh Gas Crystal", "Tear Fragment",
        "Void Essence", "Phase Thread", "Ashite Dust", "Amber Resin",
    };

    private readonly IContentProvider _content;
    private readonly Random _rng;
    private readonly int _seed;
    private readonly string _playstyle;
    private readonly MagicElement _archetype;
    private readonly string _startZoneId;

    public ProgressionSimulator(
        IContentProvider content,
        int seed,
        string playstyle,
        MagicElement archetype,
        string startZoneId)
    {
        _content = content;
        _seed = seed;
        _rng = new Random(seed);
        _playstyle = playstyle;
        _archetype = archetype;
        _startZoneId = startZoneId;
    }

    // ─── Data shapes ────────────────────────────────────────────────────────

    public sealed class SimState
    {
        public int PlayerLevel { get; set; } = 1;
        public int PlayerXp    { get; set; }
        public int CraftingSkill { get; set; } = 1;
        public int SalvageSkill  { get; set; } = 1;
        public int Gold { get; set; }
        public int PlayerMaxHp { get; set; } = 50;
        public int PlayerHp    { get; set; } = 50;

        /// <summary>item-name → qty.</summary>
        public Dictionary<string, int> Materials { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>slot → equipped workmanship.</summary>
        public Dictionary<string, int> EquippedGear { get; } = new();

        public List<CompanionState> Companions { get; } = new();

        public int TotalMaterialsEarned { get; set; }
        public int TotalMaterialsConsumed { get; set; }
    }

    public sealed class CompanionState
    {
        public string Name = "";
        public CompanionType Type;
        public MagicElement Element;
        public int Layer = 1;
        public int Level = 1;
        public int Usage;

        /// <summary>
        /// Per-type layer threshold table — sourced from
        /// <c>content/progression-curves.json</c> via
        /// <see cref="FirstMud.Domain.Configuration.ProgressionCurvesAccessor"/>
        /// so the sim and live game share one source of truth.
        /// </summary>
        private static IReadOnlyList<int> ThresholdsFor(CompanionType t) =>
            FirstMud.Domain.Configuration.ProgressionCurvesAccessor.ThresholdsFor(t);

        public int NextThreshold => Layer >= 6 ? 0 : ThresholdsFor(Type)[Layer];

        public void AddUsage(int amt)
        {
            if (Layer >= 6) return;
            Usage += amt;
            var ts = ThresholdsFor(Type);
            while (Layer < 6 && Usage >= ts[Layer])
            {
                Layer++;
                Level = Math.Max(Level, Layer * 2);
            }
        }
    }

    public sealed record ActionLogEntry(int Hour, int Slot, string Kind, string Detail);
    public sealed record HourSnapshot(
        int Hour,
        int Level,
        int CraftingSkill,
        int SalvageSkill,
        double AvgCompanionLayer,
        int EnchantingMats,
        int Gold,
        int TopGearWorkmanship,
        int ReliableDangerTier,
        int CombatWins,
        int CombatLosses,
        int Harvests,
        int Crafts,
        int Salvages);

    public sealed record SimResult(
        int Seed,
        string Playstyle,
        MagicElement Archetype,
        string StartZoneId,
        int Hours,
        IReadOnlyList<HourSnapshot> HourByHour,
        IReadOnlyList<ActionLogEntry> ActionLog,
        IReadOnlyDictionary<string, int> FinalMaterials,
        IReadOnlyDictionary<string, int> EquippedGear,
        IReadOnlyList<CompanionSummary> Companions,
        int TotalMaterialsEarned,
        int TotalMaterialsConsumed);

    public sealed record CompanionSummary(string Name, CompanionType Type, MagicElement Element, int Layer, int Level);

    // ─── Entry ──────────────────────────────────────────────────────────────

    public SimResult Run(int hours)
    {
        var state = BuildInitialState();
        var log = new List<ActionLogEntry>();
        var snapshots = new List<HourSnapshot>();

        for (int h = 1; h <= hours; h++)
        {
            int wins = 0, losses = 0, harvests = 0, crafts = 0, salvages = 0;
            int timeLeft = ActionsPerHour;
            int slot = 0;
            while (timeLeft > 0)
            {
                slot++;
                var action = PickAction(state, timeLeft);
                int cost;
                switch (action)
                {
                    case "combat":
                        cost = TimePerCombat;
                        var (won, detail) = DoCombat(state);
                        if (won) wins++; else losses++;
                        log.Add(new ActionLogEntry(h, slot, "combat", detail));
                        break;
                    case "harvest":
                        cost = TimePerHarvest;
                        var (hAmt, hMat) = DoHarvest(state);
                        harvests++;
                        log.Add(new ActionLogEntry(h, slot, "harvest", $"+{hAmt} {hMat}"));
                        break;
                    case "craft":
                        cost = TimePerCraft;
                        var cd = DoCraft(state);
                        crafts++;
                        log.Add(new ActionLogEntry(h, slot, "craft", cd));
                        break;
                    case "salvage":
                        cost = TimePerSalvage;
                        var sd = DoSalvage(state);
                        salvages++;
                        log.Add(new ActionLogEntry(h, slot, "salvage", sd));
                        break;
                    default:
                        cost = 1;
                        log.Add(new ActionLogEntry(h, slot, "idle", "no suitable action"));
                        break;
                }
                timeLeft -= cost;
            }

            // per-hour recovery
            state.PlayerHp = state.PlayerMaxHp;

            snapshots.Add(new HourSnapshot(
                h,
                state.PlayerLevel,
                state.CraftingSkill,
                state.SalvageSkill,
                state.Companions.Count == 0 ? 0 : state.Companions.Average(c => (double)c.Layer),
                TotalEnchantingMats(state),
                state.Gold,
                state.EquippedGear.Count == 0 ? 0 : state.EquippedGear.Values.Max(),
                ProbeReliableDanger(state),
                wins, losses, harvests, crafts, salvages));
        }

        return new SimResult(
            _seed, _playstyle, _archetype, _startZoneId, hours,
            snapshots, log,
            new Dictionary<string, int>(state.Materials),
            new Dictionary<string, int>(state.EquippedGear),
            state.Companions.Select(c => new CompanionSummary(c.Name, c.Type, c.Element, c.Layer, c.Level)).ToList(),
            state.TotalMaterialsEarned, state.TotalMaterialsConsumed);
    }

    // ─── Initial state ──────────────────────────────────────────────────────

    private SimState BuildInitialState()
    {
        var s = new SimState
        {
            PlayerLevel = 1,
            PlayerHp = 50,
            PlayerMaxHp = 50,
            CraftingSkill = 1,
            SalvageSkill = 1,
        };
        s.Companions.Add(new CompanionState { Name = "Ember",  Type = CompanionType.Wildfolk, Element = MagicElement.Fire,  Layer = 1, Level = 1 });
        s.Companions.Add(new CompanionState { Name = "Ripple", Type = CompanionType.Wildfolk, Element = MagicElement.Water, Layer = 1, Level = 1 });
        s.Companions.Add(new CompanionState { Name = "Mote",   Type = CompanionType.Wildfolk, Element = MagicElement.Earth, Layer = 1, Level = 1 });
        return s;
    }

    // ─── Decision ───────────────────────────────────────────────────────────

    private string PickAction(SimState state, int timeLeft)
    {
        // Playstyle-weighted choice. Weights sum to ~100.
        var weights = _playstyle switch
        {
            "combat-heavy"      => new (string k, int w)[] { ("combat", 80), ("harvest", 5),  ("craft", 10), ("salvage", 5) },
            "craft-heavy"       => new (string k, int w)[] { ("combat", 10), ("harvest", 45), ("craft", 35), ("salvage", 10) },
            "enchanting-focused"=> new (string k, int w)[] { ("combat", 45), ("harvest", 30), ("craft", 15), ("salvage", 10) },
            _                   => new (string k, int w)[] { ("combat", 40), ("harvest", 25), ("craft", 25), ("salvage", 10) },
        };

        // Filter to affordable actions
        var affordable = new List<(string k, int w)>();
        foreach (var (k, w) in weights)
        {
            int cost = k switch { "combat" => TimePerCombat, _ => 1 };
            if (cost > timeLeft) continue;
            if (k == "craft"   && !HasAnyCraftable(state)) continue;
            if (k == "salvage" && !HasSalvageable(state)) continue;
            affordable.Add((k, w));
        }
        if (affordable.Count == 0) return "idle";

        int total = affordable.Sum(x => x.w);
        int r = _rng.Next(total);
        int acc = 0;
        foreach (var (k, w) in affordable)
        {
            acc += w;
            if (r < acc) return k;
        }
        return affordable[^1].k;
    }

    // ─── Actions ────────────────────────────────────────────────────────────

    private (bool won, string detail) DoCombat(SimState state)
    {
        var zone = PickZone(state);
        if (zone is null) return (false, "no zone");
        var monster = PickMonster(zone);
        if (monster is null) return (false, $"no monster in {zone.Name}");

        var template = new MonsterTemplate(monster.Name, monster.Hp, monster.Speed, monster.Level, monster.Element, monster.Abilities);
        template = MonsterScaling.Apply(template, zone.DangerLevel, _content.CombatCurves.MonsterScaling, isBoss: false);

        var party = state.Companions
            .Take(3)
            .Select(c => new CombatSimulationService.PartyMember(c.Type, c.Element, c.Layer, c.Level))
            .ToList();

        var svc = new CombatSimulationService(new Random(_rng.Next()), _content.CombatCurves.PartyScaling);
        var enc = svc.BuildEncounter(_archetype, state.PlayerLevel, party, new[] { template });
        var res = svc.Run(enc, dangerLevel: zone.DangerLevel);

        // Award usage to active companions. Rubber-banding (task #74): scale
        // by the player-level gap so lagging layers catch up in the sim the
        // same way they do in the live game.
        foreach (var c in state.Companions.Take(3))
        {
            var mult = FirstMud.Domain.Configuration.ProgressionCurvesAccessor
                .RubberBandMultiplier(state.PlayerLevel, c.Layer);
            c.AddUsage((int)Math.Round(10 * mult));
        }

        if (res.Outcome == CombatSimulationService.Outcome.Victory)
        {
            // XP (mirror CombatHelpers: baseXp=20 * levelDiff multiplier)
            int diff = monster.Level - state.PlayerLevel;
            float mult = diff switch
            {
                >= 2 => 1.00f, 1 => 0.90f, 0 => 0.75f, -1 => 0.50f, -2 => 0.25f, -3 => 0.10f, _ => 0f
            };
            int xp = (int)(20 * mult);
            GainXp(state, xp);

            // Loot roll: base 40% + perDanger*4
            var dropCfg = _content.LootTables.DropChance;
            int dropPct = dropCfg.Base + zone.DangerLevel * dropCfg.PerDangerLevel;
            if (_rng.Next(100) < dropPct)
            {
                RollBiomeDrop(state, zone);
            }
            // Independent rolls (tapers 15%, consumables 10%)
            foreach (var ind in _content.LootTables.IndependentRolls)
            {
                if (_rng.Next(100) < ind.ChancePercent)
                {
                    var pool = _content.GetDropPool(ind.PoolId);
                    if (pool != null) RollFromPool(state, pool);
                }
            }
            // Boss drops (#140): if the picked monster is flagged isBoss,
            // roll each authored bossDrops[] entry at its dropChance. Additive
            // to the standard biome pool above.
            if (monster.IsBoss)
            {
                foreach (var bd in _content.GetBossDropsFor(monster.Id))
                {
                    if (_rng.NextDouble() < bd.DropChance)
                        AddMaterial(state, bd.ItemName, 1);
                }
            }
            return (true, $"{monster.Name}@d{zone.DangerLevel} +{xp}xp");
        }
        else
        {
            state.PlayerHp = Math.Max(1, state.PlayerMaxHp / 2);
            return (false, $"lost to {monster.Name}@d{zone.DangerLevel}");
        }
    }

    private (int amt, string mat) DoHarvest(SimState state)
    {
        var zone = PickZone(state);
        if (zone is null) return (0, "-");

        // Biome pool roll — harvest-style
        var biome = zone.Biome;
        if (!_content.LootTables.PoolByBiome.TryGetValue(biome, out var poolId))
            return (0, "-");
        var pool = _content.GetDropPool(poolId);
        if (pool is null || pool.Entries.Count == 0) return (0, "-");

        // Yield grows gently with CraftingSkill (treated as gathering proxy)
        int yield = 1 + _rng.Next(2) + state.CraftingSkill / 10;
        var entry = pool.Entries[_rng.Next(pool.Entries.Count)];
        AddMaterial(state, entry.ItemName, yield);

        // Primary resource fixed bonus from ZoneDefinition
        if (zone.PrimaryResource is { } pr)
        {
            AddMaterial(state, pr.ToString(), 1 + _rng.Next(2));
        }
        return (yield, entry.ItemName);
    }

    private string DoCraft(SimState state)
    {
        // Find best-available recipe by required skill ≤ current and all ingredients in stock
        var recipe = _content.AllRecipes()
            .Where(r => r.RequiredCraftingSkill <= state.CraftingSkill)
            .Where(r => r.Ingredients.All(i => state.Materials.GetValueOrDefault(i.Name) >= i.BaseQuantity))
            .OrderByDescending(r => r.RequiredCraftingSkill)
            .FirstOrDefault();
        if (recipe is null) return "no recipe";

        foreach (var i in recipe.Ingredients)
        {
            ConsumeMaterial(state, i.Name, i.BaseQuantity);
        }

        // skill bump
        state.CraftingSkill += 1;

        // workmanship = avg(in) + skill/20 (mirrors CraftingService formula w/o taper)
        int avgIn = 1; // materials stored at workmanship 1 in this model
        int divisor = _content.ProgressionCurves.Workmanship.SkillDivisor;
        int work = Math.Clamp(avgIn + state.CraftingSkill / divisor, recipe.BaseWorkmanshipMin, Math.Max(recipe.BaseWorkmanshipMin, recipe.BaseWorkmanshipMax));

        // Auto-equip if better than current
        string? slot = recipe.ResultCategory switch
        {
            ItemCategory.Weapon    => "MeleeWeapon",
            ItemCategory.Armor     => MapArmorSlot(recipe.ResultItemName),
            ItemCategory.Accessory => "Accessory",
            _                      => null,
        };
        if (slot != null)
        {
            var cur = state.EquippedGear.GetValueOrDefault(slot);
            if (work > cur)
            {
                state.EquippedGear[slot] = work;
                // Equipped gear gives +HP
                state.PlayerMaxHp = 50 + state.EquippedGear.Values.Sum() * 3;
            }
        }
        else
        {
            // Consumables/components into inventory
            AddMaterial(state, recipe.ResultItemName, 1);
        }

        return $"{recipe.Name} w{work}";
    }

    private string DoSalvage(SimState state)
    {
        // Pick a reagent/component we have excess of (>5) as salvage stand-in: bump salvage skill
        var surplus = state.Materials.Where(kv => kv.Value > 5).OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (surplus.Key == null) return "no salvageable";
        state.Materials[surplus.Key]--;
        state.SalvageSkill += 1;
        // 70% chance to produce a component
        if (_rng.Next(100) < 70)
        {
            AddMaterial(state, "Bone Fragment", 1);
            return $"salv {surplus.Key} → Bone Fragment";
        }
        return $"salv {surplus.Key} (fail)";
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string MapArmorSlot(string itemName)
    {
        var n = itemName.ToLowerInvariant();
        if (n.Contains("helm") || n.Contains("cap") || n.Contains("hood") || n.Contains("coif")) return "Head";
        if (n.Contains("vest") || n.Contains("chain") || n.Contains("gambeson") || n.Contains("armor")) return "Chest";
        if (n.Contains("leggings") || n.Contains("greaves") || n.Contains("trousers")) return "Legs";
        if (n.Contains("gloves") || n.Contains("vambraces") || n.Contains("handguards")) return "Hands";
        if (n.Contains("boots") || n.Contains("sabatons") || n.Contains("sandals")) return "Feet";
        return "Chest";
    }

    private void RollBiomeDrop(SimState state, ZoneDefinition zone)
    {
        var bm = _content.LootTables.BiomeMaterial;
        bool useCommon = _rng.Next(100) < bm.CommonMaterialChancePercent
                        && zone.DangerLevel < bm.DangerSuppressCommonAt;
        string poolId = useCommon
            ? "common-materials"
            : _content.LootTables.PoolByBiome.GetValueOrDefault(zone.Biome) ?? "common-materials";
        var pool = _content.GetDropPool(poolId);
        if (pool != null) RollFromPool(state, pool);
    }

    private void RollFromPool(SimState state, DropPoolDefinition pool)
    {
        if (pool.Entries.Count == 0) return;
        int totalW = pool.Entries.Sum(e => e.Weight);
        int r = _rng.Next(totalW);
        int acc = 0;
        foreach (var e in pool.Entries)
        {
            acc += e.Weight;
            if (r < acc)
            {
                int qty = Math.Max(1, e.MinQty);
                AddMaterial(state, e.ItemName, qty);
                return;
            }
        }
    }

    private void AddMaterial(SimState state, string name, int qty)
    {
        state.Materials[name] = state.Materials.GetValueOrDefault(name) + qty;
        state.TotalMaterialsEarned += qty;
    }

    private void ConsumeMaterial(SimState state, string name, int qty)
    {
        state.Materials[name] = Math.Max(0, state.Materials.GetValueOrDefault(name) - qty);
        state.TotalMaterialsConsumed += qty;
    }

    private bool HasAnyCraftable(SimState state) =>
        _content.AllRecipes().Any(r =>
            r.RequiredCraftingSkill <= state.CraftingSkill &&
            r.Ingredients.All(i => state.Materials.GetValueOrDefault(i.Name) >= i.BaseQuantity));

    private bool HasSalvageable(SimState state) =>
        state.Materials.Any(kv => kv.Value > 5);

    private void GainXp(SimState state, int xp)
    {
        state.PlayerXp += xp;
        int newLevel = (int)(1 + Math.Sqrt(state.PlayerXp / 100.0));
        if (newLevel > state.PlayerLevel)
        {
            state.PlayerLevel = newLevel;
            state.PlayerMaxHp = 50 + (newLevel - 1) * 5 + state.EquippedGear.Values.Sum() * 3;
        }
    }

    private ZoneDefinition? PickZone(SimState state)
    {
        // Pick the highest-danger zone the player could reasonably clear (>=50% probe).
        var candidates = _content.AllZones()
            .Where(z => z.World == WorldId.Aeldran)
            .Where(z => z.MonsterSpawns.Count > 0 || _content.MonstersByBiome(z.Biome).Count > 0)
            .OrderByDescending(z => z.DangerLevel)
            .ToList();
        int reliable = ProbeReliableDanger(state);
        // Choose a zone at player's effective ceiling (or 1 minimum)
        var target = candidates
            .Where(z => z.DangerLevel <= Math.Max(1, reliable + 1))
            .OrderByDescending(z => z.DangerLevel)
            .FirstOrDefault();
        return target ?? candidates.LastOrDefault();
    }

    private MonsterDefinition? PickMonster(ZoneDefinition zone)
    {
        // Prefer explicit spawns; fall back to biome+tier-appropriate monsters.
        if (zone.MonsterSpawns.Count > 0)
        {
            var id = zone.MonsterSpawns[_rng.Next(zone.MonsterSpawns.Count)].MonsterId;
            return _content.GetMonster(id);
        }
        // Tier = min(3, danger/3)
        int tier = Math.Clamp(zone.DangerLevel / 3, 0, 3);
        var pool = _content.MonstersByBiomeAndTier(zone.Biome, tier);
        if (pool.Count == 0) pool = _content.MonstersByBiome(zone.Biome);
        return pool.Count == 0 ? null : pool[_rng.Next(pool.Count)];
    }

    /// <summary>
    /// For each danger tier 1..10 runs a short simulated batch against a
    /// representative biome monster, returns the highest danger tier where
    /// win-rate ≥ 60% (reliability band).
    /// </summary>
    public int ProbeReliableDanger(SimState state)
    {
        int highest = 0;
        for (int d = 1; d <= 10; d++)
        {
            // Probe against a representative monster for this danger tier.
            // Use tier = clamp(d/3, 0..3) and pick the highest-HP monster in that
            // tier across all biomes so the probe reflects a realistic "hard
            // creature at this danger" rather than a weak goat.
            int tier = Math.Clamp(d / 3, 0, 3);
            var all = _content.AllMonsters().Where(m => m.Tier == tier).ToList();
            if (all.Count == 0) all = _content.AllMonsters().ToList();
            var mon = all.OrderByDescending(m => m.Hp).First();

            // Pack size: 1 at d1-3, 2 at d4-6, 3 at d7+. Mirrors typical live encounter scale.
            int packSize = d <= 3 ? 1 : d <= 6 ? 2 : 3;

            int wins = 0;
            int rolls = 12;
            for (int i = 0; i < rolls; i++)
            {
                var pack = new List<MonsterTemplate>();
                for (int k = 0; k < packSize; k++)
                {
                    var tmpl = new MonsterTemplate(mon.Name, mon.Hp, mon.Speed, mon.Level, mon.Element, mon.Abilities);
                    tmpl = MonsterScaling.Apply(tmpl, d, _content.CombatCurves.MonsterScaling, isBoss: false);
                    pack.Add(tmpl);
                }
                var party = state.Companions.Take(3)
                    .Select(c => new CombatSimulationService.PartyMember(c.Type, c.Element, c.Layer, c.Level))
                    .ToList();
                var svc = new CombatSimulationService(new Random(unchecked(_seed * 7919 + d * 31 + i)), _content.CombatCurves.PartyScaling);
                var enc = svc.BuildEncounter(_archetype, state.PlayerLevel, party, pack);
                var res = svc.Run(enc, dangerLevel: d);
                if (res.Outcome == CombatSimulationService.Outcome.Victory) wins++;
            }
            if ((double)wins / rolls >= 0.60) highest = d;
            else break;
        }
        return highest;
    }

    private int TotalEnchantingMats(SimState state) =>
        state.Materials.Where(kv => EnchantingMats.Contains(kv.Key)).Sum(kv => kv.Value);
}
