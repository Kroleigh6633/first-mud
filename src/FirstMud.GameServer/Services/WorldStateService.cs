using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Handlers;

namespace FirstMud.GameServer.Services;

public record TileDto(int X, int Y, string Symbol, string Name, bool IsPassable);

public record PlayerStateDto(
    Guid Id,
    string Name,
    int Level,
    int Experience,
    int CurrentHp,
    int MaxHp,
    int ActionPoints,
    int MaxActionPoints,
    float WeavePercent,
    string WeaveState,
    int X,
    int Y,
    WorldId World,
    int Strength,
    int Agility,
    int Intellect,
    int Fortitude,
    int Speed,
    int CraftingSkill,
    int SalvageSkill,
    string PrimaryElement,
    string Polarity,
    bool ElementRevealed,
    bool PolarityRevealed,
    int WyrdTangle,
    Dictionary<string, string> FactionTiers,
    List<string> ActiveCompanionIds,
    List<string> UnlockedPortals,
    List<string> CurrentQuestIds,
    int EffectiveStrength,
    int EffectiveAgility,
    int EffectiveIntellect,
    int EffectiveFortitude,
    int EffectiveSpeed,
    int EffectiveMaxHp,
    int BonusStrikeDamage,
    int BonusSpellDamage,
    List<CompanionDto> ActiveCompanions,
    int TilesDiscovered);

public record WorldStateSnapshot(
    PlayerStateDto Player,
    List<AiPlayerState> AiPlayers,
    string WorldName);

public class WorldStateService
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IQuestGraphRepository _questGraphRepository;
    private readonly AiPlayerService _aiPlayerService;
    private readonly IItemRepository _itemRepository;
    private readonly ICompanionRepository _companionRepository;

    private static readonly Dictionary<WorldId, string> WorldNames = new()
    {
        [WorldId.Aeldran]        = "Aeldran",
        [WorldId.ArdweldRemnant] = "Ardweld Remnant",
        [WorldId.FairgeanDeep]   = "Fairgean Deep",
        [WorldId.GolvariDeeps]   = "Golvari Deeps",
        [WorldId.WyrdPaths]      = "Wyrd Paths",
        [WorldId.TheDream]       = "The Dream",
    };

    public WorldStateService(
        IPlayerRepository playerRepository,
        IQuestGraphRepository questGraphRepository,
        AiPlayerService aiPlayerService,
        IItemRepository itemRepository,
        ICompanionRepository companionRepository)
    {
        _playerRepository = playerRepository;
        _questGraphRepository = questGraphRepository;
        _aiPlayerService = aiPlayerService;
        _itemRepository = itemRepository;
        _companionRepository = companionRepository;
    }

    public async Task<WorldStateSnapshot> GetSnapshotAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _playerRepository.GetByIdAsync(playerId, ct)
            ?? throw new InvalidOperationException($"Player {playerId} not found.");

        var factionTiers = player.Reputations.ToDictionary(
            r => r.FactionId.ToString(),
            r => r.Score.Tier.ToString());

        var activeCompanionIds = player.ActiveCompanionIds
            .Select(id => id.ToString())
            .ToList();

        var unlockedPortals = player.UnlockedPortals
            .Select(w => w.ToString())
            .ToList();

        // Fetch quests currently in progress for this player
        var availableQuests = await _questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
        var currentQuestIds = availableQuests
            .Where(q => q.IsTaken)
            .Select(q => q.QuestId)
            .ToList();

        // Load equipped items to compute effective stats
        var equippedItemIds = player.EquippedItems.Values.Distinct().ToList();
        var equippedItemsList = equippedItemIds.Count > 0
            ? await _itemRepository.GetByIdsAsync(equippedItemIds, ct)
            : Array.Empty<Domain.Entities.Item>();
        var equippedItemsById = equippedItemsList.ToDictionary(i => i.Id);

        // Build slot → Item lookup
        var equippedBySlot = player.EquippedItems
            .Where(kv => equippedItemsById.ContainsKey(kv.Value))
            .ToDictionary(kv => kv.Key, kv => equippedItemsById[kv.Value]);

        static int SlotW(IReadOnlyDictionary<EquipmentSlot, Domain.Entities.Item> slots, EquipmentSlot slot)
            => slots.TryGetValue(slot, out var item) ? item.Workmanship.Value : 0;

        int meleeBonus  = SlotW(equippedBySlot, EquipmentSlot.MeleeWeapon)  * 3;
        int rangedBonus = SlotW(equippedBySlot, EquipmentSlot.RangedWeapon) * 2;
        int focusBonus  = SlotW(equippedBySlot, EquipmentSlot.Focus)        * 4;
        int headBonus   = SlotW(equippedBySlot, EquipmentSlot.Head)         * 3;
        int chestBonus  = SlotW(equippedBySlot, EquipmentSlot.Chest)        * 5;
        int legsBonus   = SlotW(equippedBySlot, EquipmentSlot.Legs)         * 3;
        int handsBonus  = SlotW(equippedBySlot, EquipmentSlot.Hands)        * 2;
        int feetBonus   = SlotW(equippedBySlot, EquipmentSlot.Feet)         * 1;
        int accBonus    = SlotW(equippedBySlot, EquipmentSlot.Accessory)    * 2;

        int statStrikeBonus = player.Strength  / 5;
        int statSpellBonus  = player.Intellect / 5;
        int statFortBonus   = player.Fortitude / 2;

        int effectiveStrength   = player.Strength   + meleeBonus + handsBonus;
        int effectiveAgility    = player.Agility    + rangedBonus;
        int effectiveIntellect  = player.Intellect  + focusBonus;
        int effectiveFortitude  = player.Fortitude  + headBonus + chestBonus + legsBonus + accBonus;
        int effectiveSpeed      = player.Speed      + feetBonus;
        int effectiveMaxHp      = player.MaxHp      + headBonus + chestBonus + legsBonus + accBonus + statFortBonus;
        int bonusStrikeDamage   = meleeBonus + rangedBonus + handsBonus + statStrikeBonus;
        int bonusSpellDamage    = focusBonus + statSpellBonus;

        // Load all companions (active + homestead + idle) for the status panel
        var allCompanions = await _companionRepository.GetByOwnerAsync(playerId, ct);
        var activeCompanionDetails = allCompanions
            .Where(c => !c.IsPermanentlyGone)
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenByDescending(c => c.CurrentLayer)
            .Select(c => new CompanionDto(
                c.Id, c.Name, c.Type.ToString(), c.Element.ToString(),
                c.Level, c.CurrentLayer, c.UsageCounter, c.NextLayerThreshold,
                c.DriftAccumulator, c.IsActive, c.RelationshipDepth,
                c.AssignedDuty?.ToString(), c.DutyStartedAt))
            .ToList();

        var playerDto = new PlayerStateDto(
            player.Id,
            player.Name,
            player.Level,
            player.Experience,
            player.CurrentHp,
            player.MaxHp,
            player.ActionPoints,
            player.MaxActionPoints,
            player.Weave.Percentage,
            player.Weave.VisibleState.ToString(),
            player.Position.X,
            player.Position.Y,
            player.Position.World,
            player.Strength,
            player.Agility,
            player.Intellect,
            player.Fortitude,
            player.Speed,
            player.CraftingSkill,
            player.SalvageSkill,
            player.PrimaryElement.ToString(),
            player.Polarity.ToString(),
            player.ElementRevealed,
            player.PolarityRevealed,
            player.WyrdTangle,
            factionTiers,
            activeCompanionIds,
            unlockedPortals,
            currentQuestIds,
            effectiveStrength,
            effectiveAgility,
            effectiveIntellect,
            effectiveFortitude,
            effectiveSpeed,
            effectiveMaxHp,
            bonusStrikeDamage,
            bonusSpellDamage,
            activeCompanionDetails,
            player.TilesDiscovered);

        var aiStates = _aiPlayerService.GetAiStates().ToList();

        var worldName = WorldNames.TryGetValue(player.Position.World, out var name)
            ? name
            : player.Position.World.ToString();

        return new WorldStateSnapshot(playerDto, aiStates, worldName);
    }

    public async Task<IReadOnlyList<QuestNode>> GetAvailableQuestsAsync(Guid playerId, CancellationToken ct = default)
    {
        return await _questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
    }
}
