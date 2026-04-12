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
