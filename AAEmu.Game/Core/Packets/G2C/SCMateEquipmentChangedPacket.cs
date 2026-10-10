using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Replies to a mate equipment request and unlocks the affected slots even when refused.
/// </summary>
public class SCMateEquipmentChangedPacket : GamePacket
{
    private readonly ushort _mateTlId;
    private readonly uint _characterId;
    private readonly uint _passengerId;
    private readonly bool _bts;
    private readonly byte _num;
    private readonly bool _success;
    private readonly ItemAndLocation _first;
    private readonly ItemAndLocation _second;
    private readonly DateTime _expireTime;

    public SCMateEquipmentChangedPacket(
        ItemAndLocation first,
        ItemAndLocation second,
        ushort mateTlId,
        uint characterId,
        uint passengerId,
        bool bts,
        bool success,
        DateTime expireTime = default)
        : base(SCOffsets.SCMateEquipmentChangedPacket, 1)
    {
        _first = first;
        _second = second;
        _mateTlId = mateTlId;
        _characterId = characterId;
        _passengerId = passengerId;
        _bts = bts;
        _num = 1;
        _success = success;
        _expireTime = expireTime;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)_characterId);
        stream.Write(_mateTlId);
        stream.Write(_passengerId);
        stream.Write(_bts);
        stream.Write(_num);

        if (_first.Item == null)
            stream.Write(0u);
        else
            stream.Write(_first.Item);

        if (_second.Item == null)
            stream.Write(0u);
        else
            stream.Write(_second.Item);

        stream.Write((byte)_first.SlotType);
        stream.Write(_first.SlotNumber);
        stream.Write((byte)_second.SlotType);
        stream.Write(_second.SlotNumber);
        stream.Write(_expireTime);
        stream.Write(_success);

        return stream;
    }
}
