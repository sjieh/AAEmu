using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRequireItem : DoodadPhaseFuncTemplate
{
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint ItemId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncRequireItem");
        if (caster is Character character)
        {
            //character.Quests.OnInteraction(WorldInteractionId, character.CurrentTarget);
            if (character.Inventory.GetItemsCount(ItemId) > 0)
                return false; // Continue: the quest matches and the character has the required item.
            else
                return true; // Stop: the quest does not match and the character lacks the required item.
        }
        return true; // Stop: the quest does not match and the character lacks the required item.
    }
}
