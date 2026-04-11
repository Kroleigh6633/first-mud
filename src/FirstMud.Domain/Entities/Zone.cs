using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Entities;

public class Zone
{
    public Guid Id { get; private set; }
    public WorldId WorldId { get; private set; }
    public int ZoneId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string AsciiSymbol { get; private set; } = string.Empty;
    public int DangerLevel { get; private set; }
    public bool IsPortalZone { get; private set; }
    public WorldId? PortalDestination { get; private set; }

    private readonly List<string> _lootTableIds = [];
    public IReadOnlyList<string> LootTableIds => _lootTableIds.AsReadOnly();

    private readonly List<string> _encounterTableIds = [];
    public IReadOnlyList<string> EncounterTableIds => _encounterTableIds.AsReadOnly();

    private Zone() { }

    public static Zone Create(
        WorldId worldId,
        int zoneId,
        string name,
        string description,
        string asciiSymbol,
        int dangerLevel,
        bool isPortalZone = false,
        WorldId? portalDestination = null,
        IEnumerable<string>? lootTableIds = null,
        IEnumerable<string>? encounterTableIds = null)
    {
        if (asciiSymbol.Length != 1)
            throw new ArgumentException("AsciiSymbol must be exactly 1 character.", nameof(asciiSymbol));
        if (dangerLevel < 1 || dangerLevel > 10)
            throw new ArgumentOutOfRangeException(nameof(dangerLevel), "DangerLevel must be between 1 and 10.");

        var zone = new Zone
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            ZoneId = zoneId,
            Name = name,
            Description = description,
            AsciiSymbol = asciiSymbol,
            DangerLevel = dangerLevel,
            IsPortalZone = isPortalZone,
            PortalDestination = portalDestination
        };

        if (lootTableIds is not null)
            zone._lootTableIds.AddRange(lootTableIds);
        if (encounterTableIds is not null)
            zone._encounterTableIds.AddRange(encounterTableIds);

        return zone;
    }
}
