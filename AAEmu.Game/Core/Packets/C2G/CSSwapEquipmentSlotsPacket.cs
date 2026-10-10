using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Exchanges the character's main-hand and off-hand equipment slots.
/// </summary>
/// <remarks>
/// The 10.0.2 client sends a six-byte body: a 32-bit value followed by the two slot indices.
/// The captured request for slots 15 and 16 carries 2 in that first field; its purpose is not
/// established yet, so the server uses its own current equipment contents as authoritative.
/// </remarks>
public class CSSwapEquipmentSlotsPacket() : GamePacket(CSOffsets.CSSwapEquipmentSlotsPacket, 1)
{
    private const int BodySize = sizeof(uint) + sizeof(byte) * 2;

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte equipment-slot swap body, got {stream.LeftBytes} bytes.");

        var wireValue = stream.ReadUInt32();
        var fromSlot = stream.ReadByte();
        var toSlot = stream.ReadByte();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var mainhand = (byte)EquipmentItemSlot.Mainhand;
        var offhand = (byte)EquipmentItemSlot.Offhand;
        if (fromSlot == toSlot ||
            !((fromSlot == mainhand && toSlot == offhand) || (fromSlot == offhand && toSlot == mainhand)))
        {
            Logger.Warn(
                "CSSwapEquipmentSlotsPacket - Unsupported slot pair {0} <-> {1} (wire value {2})",
                fromSlot, toSlot, wireValue);
            character.SendErrorMessage(ErrorMessageType.InvalidSlot);
            return;
        }

        var equipment = character.Inventory.Equipment;
        var fromItem = equipment.GetItemBySlot(fromSlot);
        var toItem = equipment.GetItemBySlot(toSlot);
        if (fromItem == null && toItem == null)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidSlot);
            return;
        }

        // This request names the two positions rather than an item direction. Normalize it so
        // the generic inventory path always receives an occupied source slot.
        if (fromItem == null)
        {
            (fromSlot, toSlot) = (toSlot, fromSlot);
            (fromItem, toItem) = (toItem, fromItem);
        }

        // The generic inventory path applies the normal equip-slot rules and performs the usual
        // equipment broadcasts, stat updates, and persistence for both items.
        if (!character.Inventory.SplitOrMoveItem(
                ItemTaskType.SwapItems,
                fromItem?.Id ?? 0, SlotType.Equipment, fromSlot,
                toItem?.Id ?? 0, SlotType.Equipment, toSlot))
        {
            character.SendErrorMessage(ErrorMessageType.InvalidSlot);
        }
    }
}
