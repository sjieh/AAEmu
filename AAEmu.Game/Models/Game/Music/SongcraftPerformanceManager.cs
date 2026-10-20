using System.Runtime.CompilerServices;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Music;

/// <summary>
/// Keeps a Songcraft performance timeline active for the lifetime of its own timed performance buff.
/// </summary>
/// <remarks>
/// The periodic maintenance skill preserves the active performance buffs. The server defers both
/// skill-ended signals until that buff times out or is removed early.
/// </remarks>
public static class SongcraftPerformanceManager
{
    private const uint PerformanceMaintenanceSkillId = 45992;

    private static readonly HashSet<uint> PerformanceSkillIds = [10723, 10724, 10727, 11396];
    private static readonly object RegistrationLock = new();
    private static readonly ConditionalWeakTable<Buff, TimeoutRegistration> Registrations = new();
    private static readonly Dictionary<uint, Skill> ActivePerformanceCasts = [];
    private static readonly HashSet<Skill> DeferredClientEnds = [];

    public static bool IsPerformanceBuff(uint skillId, BuffTemplate buffTemplate) =>
        PerformanceSkillIds.Contains(skillId) &&
        buffTemplate is { Duration: > 0 } &&
        (buffTemplate.StringInstrumentStartAnimId > 0 ||
         buffTemplate.PercussionInstrumentStartAnimId > 0 ||
         buffTemplate.TubeInstrumentStartAnimId > 0);

    /// <summary>
    /// The periodic maintenance tick casts its hidden helper skill through a buff-template effect
    /// source, so it has no originating Skill. Both maintenance buffs (17205 and 17207) call the
    /// same helper, so identify the tick by its data-defined skill id and source shape.
    /// </summary>
    public static bool IsPerformanceMaintenanceTick(int skillId, Skill sourceSkill) =>
        skillId == PerformanceMaintenanceSkillId && sourceSkill == null;

    public static bool IsPerformanceMaintenanceSkill(uint skillId) =>
        skillId == PerformanceMaintenanceSkillId;

    public static void Observe(Buff buff, Character owner, uint skillId)
    {
        if (buff == null || owner == null || !IsPerformanceBuff(skillId, buff.Template) ||
            buff.Duration <= 0 || buff.Owner?.ObjId != owner.ObjId)
            return;

        var skill = buff.Skill;
        Skill previous = null;
        lock (RegistrationLock)
        {
            if (Registrations.TryGetValue(buff, out var registration))
            {
                registration.Skill = skill;
            }
            else
            {
                registration = new TimeoutRegistration(owner, skill);
                Registrations.Add(buff, registration);
                buff.Events.OnTimeout += registration.OnTimeout;
                buff.Events.OnDispelled += registration.OnDispelled;
            }

            if (skill != null && skill.TlId != 0)
            {
                if (ActivePerformanceCasts.TryGetValue(owner.ObjId, out var active) &&
                    !ReferenceEquals(active, skill))
                {
                    previous = active;
                    DeferredClientEnds.Remove(previous);
                }

                ActivePerformanceCasts[owner.ObjId] = skill;
                DeferredClientEnds.Add(skill);
            }
        }

        previous?.CompleteDeferredClientEnd(owner);
    }

    private static bool Forget(Buff buff, TimeoutRegistration registration)
    {
        lock (RegistrationLock)
        {
            if (!Registrations.TryGetValue(buff, out var current) || !ReferenceEquals(current, registration))
                return false;

            Registrations.Remove(buff);
            buff.Events.OnTimeout -= registration.OnTimeout;
            buff.Events.OnDispelled -= registration.OnDispelled;
            return true;
        }
    }

    public static bool ShouldDeferClientEnd(Skill skill)
    {
        lock (RegistrationLock)
            return DeferredClientEnds.Contains(skill);
    }

    /// <summary>
    /// A new cast takes over the character's animation. End the old timeline now, while leaving its
    /// performance buff registered until its normal timeout or removal.
    /// </summary>
    public static void EndActiveClientAnimation(Unit owner)
    {
        if (owner == null)
            return;

        Skill active = null;
        lock (RegistrationLock)
        {
            if (ActivePerformanceCasts.Remove(owner.ObjId, out active))
                DeferredClientEnds.Remove(active);
        }

        if (owner is Character character)
            active?.CompleteDeferredClientEnd(character);
    }

    private static void EndClientAnimation(Character owner, Skill skill)
    {
        if (owner == null || skill == null)
            return;

        lock (RegistrationLock)
        {
            DeferredClientEnds.Remove(skill);
            if (ActivePerformanceCasts.TryGetValue(owner.ObjId, out var active) &&
                ReferenceEquals(active, skill))
                ActivePerformanceCasts.Remove(owner.ObjId);
        }

        skill.CompleteDeferredClientEnd(owner);
    }

    private sealed class TimeoutRegistration(Character owner, Skill skill)
    {
        public Skill Skill { get; set; } = skill;

        public void OnTimeout(object sender, OnTimeoutArgs args)
        {
            if (sender is not Buff buff || !Forget(buff, this))
                return;

            EndClientAnimation(owner, Skill);
        }

        public void OnDispelled(object sender, OnDispelledArgs args)
        {
            if (sender is Buff buff)
            {
                if (Forget(buff, this))
                    EndClientAnimation(owner, Skill);
            }
        }
    }
}
