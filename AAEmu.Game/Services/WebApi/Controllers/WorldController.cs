using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Services.WebApi.Models;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

/// <summary>
/// Character controller for the WebApi
/// </summary>6
internal class WorldController : BaseController
{
    [WebApiGet("/api/world/logged-characters")]
    public HttpResponse GetCharacter(HttpRequest request)
    {
        var loggedCharacters = WorldManager.Instance.GetAllCharacters()
            .Select(x => new CharacterModel(x.Id, x.Name, x.Level, x.Created, x.IsOnline));
        return OkJson(loggedCharacters);
    }

    [WebApiGet("/api/world/character-last-zone")]
    public HttpResponse GetCharacterLastZone(HttpRequest request)
    {
        var characterName = ParseQueryString(request.Url).Get("name")?.Trim();
        if (string.IsNullOrWhiteSpace(characterName))
            return BadRequestJson(new { error = "A character name is required." });

        // Prefer the live transform for an online character; the saved row can lag behind it.
        var onlineCharacter = WorldManager.Instance.GetAllCharacters()
            .FirstOrDefault(character => character.IsOnline &&
                string.Equals(character.Name, characterName, StringComparison.OrdinalIgnoreCase));
        if (onlineCharacter != null)
        {
            return OkJson(new CharacterLastZoneModel(
                onlineCharacter.Name, onlineCharacter.Transform?.ZoneId ?? 0, true));
        }

        // The World server owns the database configuration. Zone Manager asks this API rather than
        // receiving or storing MySQL credentials of its own.
        using var connection = AAEmu.Commons.Utils.DB.MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT `name`, `zone_id` FROM `characters` WHERE `name` = @name AND `deleted` = 0 LIMIT 1";
        command.Parameters.AddWithValue("@name", characterName);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return BadRequestJson(new { error = $"Character '{characterName}' was not found." });

        return OkJson(new CharacterLastZoneModel(
            reader.GetString("name"), reader.GetUInt32("zone_id"), false));
    }

    [WebApiGet("/api/world/zone-manager-status")]
    public HttpResponse GetZoneManagerStatus(HttpRequest request)
    {
        var players = WorldManager.Instance.GetAllCharacters()
            .Where(character => character.IsOnline)
            .Select(character =>
            {
                var zoneKey = character.Transform?.ZoneId ?? 0;
                var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
                // World position drives the Zone Manager's proximity pre-start: a zone is brought
                // up while the nearest player is still short of its border, not on arrival.
                var position = character.Transform?.World.Position ?? default;
                return new WorldPlayerStatusModel(
                    character.Id,
                    character.ObjId,
                    character.Name,
                    character.Level,
                    zoneKey,
                    zone?.Name ?? "Unknown",
                    character.Transform?.InstanceId ?? 0,
                    position.X,
                    position.Y);
            })
            .OrderBy(character => character.Name)
            .ToArray();

        var zones = WorldIntegration.GetZoneConnectionStatus?.Invoke() ?? [];
        var portalDestinationZoneKeys = AAEmu.Game.Core.Managers.PortalManager.Instance
            .GetOpenPortalDestinationZoneIds();
        return OkJson(new WorldStatusModel(
            DateTime.UtcNow,
            Program.UpTime,
            players.Length,
            players,
            zones,
            portalDestinationZoneKeys));
    }
}
