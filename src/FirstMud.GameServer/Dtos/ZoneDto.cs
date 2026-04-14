namespace FirstMud.GameServer.Dtos;

public record ZoneTileDto(
    string WorldId,
    int ZoneId,
    string Name,
    string Description,
    string AsciiSymbol,
    int DangerLevel,
    bool IsPortalZone,
    int X,
    int Y);

public record ZoneViewDto(List<ZoneTileDto> Tiles);

/// <summary>
/// One rumor per zone — a deterministic pick from the authored rumor pool
/// keyed by <c>(zoneContentId, dayIndex)</c>. Client uses <c>zoneName</c> to
/// look up the rumor for the tile under the cursor (zone tiles carry
/// <c>name</c> but not the content-layer string zoneId).
/// </summary>
public record ZoneRumorEntryDto(string ZoneContentId, string ZoneName, string Rumor);

public record ZoneRumorsDto(List<ZoneRumorEntryDto> Rumors);
