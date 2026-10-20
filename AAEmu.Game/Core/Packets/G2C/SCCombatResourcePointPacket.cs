using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using NLog;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Combat resource total for a unit — what drives the combo-pip UI.
/// </summary>
/// <remarks>
/// 39c654a0. Slot sequence 0x40,0x40,0x1a8,0x1a0,0x80,0x98,0x80: the two 0x40s are the direction probe and
/// carry no wire field, the 0x1a8/0x1a0 pair is the write/read side of one 3-byte compressed object id, and
/// the remaining three are i32, u64 "point" and i32 "updateTime".
/// </remarks>
public class SCCombatResourcePointPacket(uint objId, int combatResourceId, ulong point, int updateTime)
    : GamePacket(SCOffsets.SCCombatResourcePointPacket, 1)
{
    private static readonly Logger CombatResourceLogger = LogManager.GetLogger("CombatResource");

    public override PacketStream Write(PacketStream stream)
    {
        var payloadStart = stream.Count;
        stream.WriteBc(objId);
        stream.Write(combatResourceId);
        stream.Write(point);
        stream.Write(updateTime);
        var payloadHex = Convert.ToHexString(stream.Buffer.AsSpan(payloadStart, stream.Count - payloadStart));
        CombatResourceLogger.Debug("Serialized SCCombatResourcePoint 0x{0:X3}: unit {1}, resourceId {2}, point {3}, updateTime {4}, bodyHex {5}",
            SCOffsets.SCCombatResourcePointPacket, objId, combatResourceId, point, updateTime, payloadHex);
        return stream;
    }
}
