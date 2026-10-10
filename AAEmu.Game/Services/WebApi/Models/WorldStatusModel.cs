namespace AAEmu.Game.Services.WebApi.Models;

internal sealed record WorldStatusModel(
    DateTime ServerTimeUtc,
    int UptimeSeconds,
    int PlayerCount,
    IReadOnlyList<WorldPlayerStatusModel> Players,
    IReadOnlyList<WorldZoneConnectionSnapshot> Zones,
    IReadOnlyList<uint> PortalDestinationZoneKeys);

internal sealed record WorldPlayerStatusModel(
    uint Id,
    uint ObjectId,
    string Name,
    byte Level,
    uint ZoneKey,
    string ZoneName,
    uint InstanceId,
    float X,
    float Y);

internal sealed record CharacterLastZoneModel(string Name, uint ZoneKey, bool IsOnline);
