using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Charges the caster the mana amount carried by this plot effect. The plot decides when the cost applies
/// and may select a different amount for different targets. See <see cref="ManaCostRules"/> for the formula.
/// </summary>
public class ManaCost : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ManaCost;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        character.ReduceCurrentMp(null, ManaCostRules.ModifiedCost(character, skill, value1, value2));

        character.LastCast = DateTime.UtcNow;
        character.IsInPostCast = true;
    }
}
