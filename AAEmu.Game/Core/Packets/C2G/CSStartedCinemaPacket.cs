using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartedCinemaPacket() : GamePacket(CSOffsets.CSStartedCinemaPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var cinemaId = character.CurrentlyPlayingCinemaId;
        // The client can enter a cutscene from a trigger that did not pass through the
        // server-side cinema binding path. Dismount here as a final synchronization point.
        if (character.ForceDismount())
            Logger.Info("Dismounted {0} when cinema {1} started", character.Name, cinemaId);

        Logger.Warn("StartedCinema cinema={0}", cinemaId);
        character.Events.OnCinemaStarted(character, new OnCinemaStartedArgs { CinemaId = cinemaId });
    }
}
