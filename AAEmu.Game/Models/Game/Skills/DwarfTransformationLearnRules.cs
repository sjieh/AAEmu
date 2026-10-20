using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Dwarf Juggernaut transformation behavior observed on the 10.0.2 client, including the special
/// learn cast, level-based skill progression, and the temporary form skill grants.
/// </summary>
public static class DwarfTransformationLearnRules
{
    public const int DwarfRaceId = 3;
    public const AbilityType JuggernautAbilityId = AbilityType.Trooper;
    public const uint LearnTransformationSkillId = 33995;
    public const uint SummonJuggernautSkillId = 33985;
    public const uint CancelTransformationSkillId = 35429;
    public const uint JuggernautTransformationBuffId = 20392;
    public const uint JuggernautTransformationBuffTagId = 2840;
    public const byte LearnTransformationFlag = 19;

    public static bool IsLearnRequest(uint skillId, byte flag) =>
        skillId == LearnTransformationSkillId && flag == LearnTransformationFlag;

    public static bool IsLearnCast(uint skillId, SkillObject skillObject) =>
        skillId == LearnTransformationSkillId && skillObject is SkillObjectDwarfTransformationLearn;

    /// <summary>
    /// Whether the active Juggernaut form buff grants this skill to a Dwarf. Skills granted by the
    /// form must be usable while the form is active, even though their shared skill tag also carries
    /// the generic "cannot use while transformed" requirement.
    /// </summary>
    public static bool IsActiveJuggernautFormGrant(BaseUnit caster, uint skillId) =>
        caster is Character character &&
        (int)character.Race == DwarfRaceId &&
        character.Buffs.CheckBuff(JuggernautTransformationBuffId) &&
        SkillManager.Instance.GetBuffGrantSet(JuggernautTransformationBuffId).GrantedSkills.Contains(skillId);

    /// <summary>Matches the caster-side prohibition that conflicts with the Dwarf form's own grants.</summary>
    public static bool IsJuggernautFormCastBlocker(in SkillRequirement requirement) =>
        !requirement.OnTarget &&
        !requirement.Require &&
        requirement.BuffId == 0 &&
        requirement.BuffTagId == JuggernautTransformationBuffTagId;
}
