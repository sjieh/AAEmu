using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveChatChannelPacket() : GamePacket(CSOffsets.CSLeaveChatChannelPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var chat = stream.ReadInt64(); // TODO: Decode this value.

        Logger.Debug("LeaveChatChannel, Chat: {0}", chat);
    }
}
