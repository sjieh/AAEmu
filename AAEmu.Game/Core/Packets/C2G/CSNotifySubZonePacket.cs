using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifySubZonePacket() : GamePacket(CSOffsets.CSNotifySubZonePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var subZoneId = stream.ReadUInt32();
        if (subZoneId == 0) return;

        var character = Connection.ActiveChar;
        var previousSubZoneId = character.SubZoneId;
        character.SubZoneId = subZoneId; // needed to store Memory Tome points for Recall

        var position = character.Transform.World.Position;
        Logger.Info(
            $"Subzone entered: {character.Name} ({character.Id}) subZoneId={subZoneId} " +
            $"previous={previousSubZoneId} world={character.Transform.WorldId} zone={character.Transform.ZoneId} " +
            $"position=({position.X:F1}, {position.Y:F1}, {position.Z:F1})");

        character.Portals.NotifySubZone(subZoneId);
    }
}
