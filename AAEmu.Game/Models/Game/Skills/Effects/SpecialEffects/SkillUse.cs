using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Music;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SkillUse : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SkillUse;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int skillId,
        int delay,
        int chance,
        int value4)
    {
        Logger.Debug("Special effects: SkillUse skillId {0}, delay {1}, value3 {2}, value4 {3} caster={4}",
            skillId, delay, chance, value4, caster?.ObjId ?? 0);

        if (Random.Shared.Next(0, 100) > chance && chance != 0)
        {
            caster.ConditionChance = false;
            return;
        }
        else
        {
            caster.ConditionChance = true;
        }

        //target = ((Unit)caster).CurrentTarget;
        var useSkill = new Skill(SkillManager.Instance.GetSkillTemplate((uint)skillId));
        targetObj = new SkillCastUnitTarget(target?.ObjId ?? 0);
        // The periodic Songcraft helper is maintenance work, not a player skill use that should
        // consume the performance buff it is keeping alive.
        if (!SongcraftPerformanceManager.IsPerformanceMaintenanceTick(skillId, skill))
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.UseSkill);
        TaskManager.Instance.Schedule(new UseSkillTask(useSkill, caster, casterObj, target, targetObj, skillObject), TimeSpan.FromMilliseconds(delay));
        //useSkill.ApplyEffects(caster, casterObj, target, targetObj, skillObject);
    }
}
