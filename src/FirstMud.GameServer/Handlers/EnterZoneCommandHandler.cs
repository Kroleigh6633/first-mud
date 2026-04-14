using FirstMud.Application.Content;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Dtos;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class EnterZoneCommandHandler(
    IPlayerRepository playerRepository,
    IZoneRepository zoneRepository,
    IContentProvider content,
    GameNotificationService notificationService) : ICommandHandler<EnterZoneCommand>
{
    public async Task<CommandResult> HandleAsync(EnterZoneCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var worldId = player.Position.World;
        var zones = await zoneRepository.GetByWorldAsync(worldId, ct);

        var tiles = zones.Select(z =>
        {
            var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
            var (x, y) = known ?? ZoneGridLayout.GetPosition(z.Id);
            return new ZoneTileDto(
                z.WorldId.ToString(),
                z.ZoneId,
                z.Name,
                z.Description,
                z.AsciiSymbol,
                z.DangerLevel,
                z.IsPortalZone,
                x,
                y);
        }).ToList();

        var viewDto = new ZoneViewDto(tiles);

        await notificationService.SendEventAsync(cmd.PlayerId, "ZoneView", viewDto, ct);

        // Rumor pass — pick a deterministic rumor per zone that has authored
        // flavor text. Client surfaces these on unexplored tiles so the world
        // map whispers a hint of what lies beyond fog-of-war without leaking
        // biome, danger, or spawn data.
        var rumorsDto = BuildZoneRumorsDto(worldId, zones);
        if (rumorsDto.Rumors.Count > 0)
            await notificationService.SendEventAsync(cmd.PlayerId, "ZoneRumors", rumorsDto, ct);

        return new CommandResult(true, $"Zone view sent with {tiles.Count} tiles.", viewDto);
    }

    private ZoneRumorsDto BuildZoneRumorsDto(
        FirstMud.Domain.Enums.WorldId worldId,
        IReadOnlyList<FirstMud.Domain.Entities.Zone> zones)
    {
        // Deterministic day index — UTC days since Unix epoch. Stable within a
        // 24-hour window, rotates overnight. Good enough for "same rumor all
        // play session, different tomorrow."
        var dayIndex = (int)(DateTime.UtcNow.Date - DateTime.UnixEpoch).TotalDays;

        var entries = new List<ZoneRumorEntryDto>();
        foreach (var zone in zones)
        {
            // Content zoneId is the string key in zones.json keyed by
            // (WorldId, ZoneNumber). The zone-rumors pool is keyed by that
            // same string id. Fall back silently when no definition exists —
            // seeded zones that predate the content layer shouldn't crash.
            var def = content.AllZones().FirstOrDefault(z =>
                z.World == worldId && z.ZoneNumber == zone.ZoneId);
            if (def is null) continue;

            var pool = content.GetZoneRumors(def.ZoneId);
            if (pool.Count == 0) continue;

            var idx = PickDeterministicIndex(def.ZoneId, dayIndex, pool.Count);
            entries.Add(new ZoneRumorEntryDto(def.ZoneId, zone.Name, pool[idx]));
        }

        return new ZoneRumorsDto(entries);
    }

    /// <summary>
    /// Stable hash of (zoneId, dayIndex) modulo <paramref name="count"/>.
    /// Deliberately not <c>string.GetHashCode()</c> — runtime-randomized hash
    /// seeds would make the selection non-reproducible across processes.
    /// </summary>
    private static int PickDeterministicIndex(string zoneId, int dayIndex, int count)
    {
        unchecked
        {
            uint h = 2166136261u; // FNV-1a 32-bit offset basis
            foreach (var c in zoneId)
            {
                h ^= c;
                h *= 16777619u;
            }
            h ^= (uint)dayIndex;
            h *= 16777619u;
            return (int)(h % (uint)count);
        }
    }
}
