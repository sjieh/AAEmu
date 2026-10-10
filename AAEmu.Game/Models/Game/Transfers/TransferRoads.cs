using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Transfers;

public class TransferRoads
{
    public string Name { get; set; }
    public uint ZoneId { get; set; }
    public int Type { get; set; } // When owner_id != 0, this is the route's starting segment and the transport TemplateId for that route.
    public int CellX { get; set; }
    public int CellY { get; set; }
    public List<WorldSpawnPosition> Pos { get; set; } = [];
}
