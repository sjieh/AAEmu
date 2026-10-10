using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public class Plot
{
    private bool? _hasManaCostEffect;

    public uint Id { get; set; }
    public uint TargetTypeId { get; set; }

    // Probably not needed anymore
    public PlotEventTemplate EventTemplate { get; set; }

    public PlotTree Tree { get; set; }

    /// <summary>
    /// Whether this plot contains a ManaCost special effect. Such effects carry the actual,
    /// possibly target-dependent, cost and must not be combined with skills.mana_cost.
    /// </summary>
    public bool HasManaCostEffect => _hasManaCostEffect ??= ContainsManaCostEffect();

    private bool ContainsManaCostEffect()
    {
        if (Tree?.RootNode == null)
            return false;

        var pending = new Stack<PlotNode>();
        var visited = new HashSet<PlotNode>();
        pending.Push(Tree.RootNode);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!visited.Add(node))
                continue;

            if (node.Event?.Effects.Any(effect =>
                    effect.ActualType == "SpecialEffect" &&
                    SkillManager.Instance.GetEffectTemplate(effect.ActualId, effect.ActualType)
                        is SpecialEffect specialEffect &&
                    specialEffect.SpecialEffectTypeId == SpecialType.ManaCost) == true)
                return true;

            foreach (var child in node.Children)
                pending.Push(child);
        }

        return false;
    }

    /// <summary>
    /// Estimates the mana this cast's plot can charge before the plot starts. Conditions that are safe to
    /// evaluate are followed with the actual caster and target; stateful or random conditions are treated as
    /// either outcome so their possible costs are included. Plot effects are deliberately not executed here.
    /// </summary>
    public int GetRequiredManaCost(PlotState state)
    {
        if (state?.Caster == null || state.ActiveSkill == null || Tree?.RootNode == null)
            return 0;

        var pending = new Queue<ManaCostVisit>();
        pending.Enqueue(new ManaCostVisit(Tree.RootNode, state.Caster, state.Target));
        var visited = new HashSet<ManaCostVisitKey>();
        long totalCost = 0;

        while (pending.Count > 0)
        {
            var visit = pending.Dequeue();
            var node = visit.Node;
            if (node?.Event == null)
                continue;

            var (source, sourceKnown) = SelectSource(
                node.Event.SourceUpdateMethodId, state, visit.PreviousSource, visit.PreviousTarget);
            var (target, targetKnown) = SelectTarget(
                node.Event.TargetUpdateMethodId, state, visit.PreviousSource, visit.PreviousTarget);
            var key = new ManaCostVisitKey(node.Event.Id, source?.ObjId ?? 0, target?.ObjId ?? 0);
            if (!visited.Add(key))
                continue;

            var conditionResult = EvaluateConditions(state, node.Event, source, sourceKnown, target, targetKnown);
            if (conditionResult.CanSucceed)
            {
                foreach (var effect in node.Event.Effects)
                {
                    if (effect.ActualType != "SpecialEffect" ||
                        SkillManager.Instance.GetEffectTemplate(effect.ActualId, effect.ActualType) is not SpecialEffect specialEffect ||
                        specialEffect.SpecialEffectTypeId != SpecialType.ManaCost)
                        continue;

                    var effectCost = ManaCostRules.ModifiedCost(
                        state.Caster, state.ActiveSkill, specialEffect.Value1, specialEffect.Value2);
                    var repeatCount = Math.Max(1, state.ActiveSkill.Template.EffectRepeatCount);
                    totalCost += (long)effectCost * repeatCount;
                }
            }

            foreach (var child in node.Children)
            {
                var failEdge = child?.ParentNextEvent?.Fail ?? false;
                if (failEdge ? !conditionResult.CanFail : !conditionResult.CanSucceed)
                    continue;

                // The target info passed to a child starts from this event's selected source and target.
                // Area and random target methods are unknown here; conditions depending on those targets
                // remain conservative in EvaluateConditions.
                pending.Enqueue(new ManaCostVisit(child, sourceKnown ? source : null, targetKnown ? target : null));
            }
        }

        return (int)Math.Clamp(totalCost, int.MinValue, int.MaxValue);
    }

    private static (BaseUnit Unit, bool Known) SelectSource(
        uint method, PlotState state, BaseUnit previousSource, BaseUnit previousTarget) =>
        (PlotSourceUpdateMethodType)method switch
        {
            PlotSourceUpdateMethodType.OriginalSource => (state.Caster, true),
            PlotSourceUpdateMethodType.OriginalTarget => (state.Target, true),
            PlotSourceUpdateMethodType.PreviousSource => (previousSource, previousSource != null),
            PlotSourceUpdateMethodType.PreviousTarget => (previousTarget, previousTarget != null),
            _ => (null, false)
        };

    private static (BaseUnit Unit, bool Known) SelectTarget(
        uint method, PlotState state, BaseUnit previousSource, BaseUnit previousTarget) =>
        (PlotTargetUpdateMethodType)method switch
        {
            PlotTargetUpdateMethodType.OriginalSource => (state.Caster, true),
            PlotTargetUpdateMethodType.OriginalTarget => (state.Target, true),
            PlotTargetUpdateMethodType.PreviousSource => (previousSource, previousSource != null),
            PlotTargetUpdateMethodType.PreviousTarget => (previousTarget, previousTarget != null),
            _ => (null, false)
        };

    private static (bool CanSucceed, bool CanFail) EvaluateConditions(
        PlotState state,
        PlotEventTemplate plotEvent,
        BaseUnit source,
        bool sourceKnown,
        BaseUnit target,
        bool targetKnown)
    {
        var canSucceed = true;
        var canFail = false;

        foreach (var eventCondition in plotEvent.Conditions)
        {
            var result = EvaluateCondition(state, eventCondition, source, sourceKnown, target, targetKnown);
            if (result == false)
            {
                canSucceed = false;
                canFail = true;
            }
            else if (!result.HasValue)
            {
                canFail = true;
            }
        }

        return (canSucceed, canFail);
    }

    private static bool? EvaluateCondition(
        PlotState state,
        PlotEventCondition eventCondition,
        BaseUnit source,
        bool sourceKnown,
        BaseUnit target,
        bool targetKnown)
    {
        var condition = eventCondition?.Condition;
        if (condition == null || !CanEvaluateWithoutSideEffects(condition.Kind))
            return null;

        var conditionSource = eventCondition.SourceId switch
        {
            PlotEffectSource.OriginalSource => state.Caster,
            PlotEffectSource.OriginalTarget => state.Target,
            PlotEffectSource.Source when sourceKnown => source,
            PlotEffectSource.Target when targetKnown => target,
            _ => null
        };
        var conditionTarget = eventCondition.TargetId switch
        {
            PlotEffectTarget.OriginalSource => state.Caster,
            PlotEffectTarget.OriginalTarget => state.Target,
            PlotEffectTarget.Source when sourceKnown => source,
            PlotEffectTarget.Target when targetKnown => target,
            PlotEffectTarget.Location when targetKnown => target,
            _ => null
        };

        if (conditionSource == null || conditionTarget == null)
            return null;

        try
        {
            return condition.Check(conditionSource, state.CasterCaster, conditionTarget,
                state.TargetCaster, state.SkillObject, state.ActiveSkill);
        }
        catch
        {
            // A preflight estimate must not stop a cast because a condition requires plot state that is not
            // available until execution. Treat it as unresolved and include both possible branches.
            return null;
        }
    }

    private static bool CanEvaluateWithoutSideEffects(PlotConditionType kind) => kind switch
    {
        PlotConditionType.Chance or PlotConditionType.CombatDiceResult or PlotConditionType.Variable or
            PlotConditionType.CastingUseable => false,
        _ => true
    };

    private readonly record struct ManaCostVisit(PlotNode Node, BaseUnit PreviousSource, BaseUnit PreviousTarget);
    private readonly record struct ManaCostVisitKey(uint EventId, uint SourceId, uint TargetId);

    public async Task RunAsync(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster,
        SkillObject skillObject, Skill skill, ushort castTlId)
    {
        if (caster is not Unit casterUnit)
            return;

        // New cast-time or channel plot while the previous one is still on the bar: cancel so
        // anims / projectiles do not stack. Sport-fish holds replace each other immediately but
        // must not tear down the rod channel (plots 809 / 821).
        var prev = casterUnit.ActivePlotState;
        if (prev != null && !ReferenceEquals(prev.ActiveSkill, skill))
        {
            var incomingTags = SkillManager.Instance.GetSkillTags(skill.Id);
            var prevSkill = prev.ActiveSkill;
            var prevTags = prevSkill != null ? SkillManager.Instance.GetSkillTags(prevSkill.Id) : null;
            var incomingHold = SportFishCombat.IsFishingHoldSkill(skill.Template.TargetType, incomingTags);
            var prevHold = prevSkill?.Template != null &&
                           SportFishCombat.IsFishingHoldSkill(prevSkill.Template.TargetType, prevTags);
            var incomingCombo = skill.Template != null &&
                                SkillCastOverlapRules.IsInstantComboHit(
                                    skill.Template.CastingTime, skill.Template.CustomGcd);
            var incomingSame = prevSkill != null && prevSkill.Id == skill.Id;
            var previousIsFollowUp = prevSkill != null &&
                                     SkillComboRules.IsComboFollowUpOf(
                                         skill.Id, prevSkill.Id, SkillComboRules.NextFollowUp);
            if (SkillCastOverlapRules.ShouldCancelPreviousPlot(
                    prev.IsCasting || prev.IsChanneling,
                    incomingCombo,
                    incomingSame,
                    SportFishCombat.ShouldCancelPreviousPlot(
                        prev.IsCasting || prev.IsChanneling,
                        prevHold,
                        incomingHold),
                    previousIsFollowUp))
                prev.RequestCancellation();
        }

        var state = new PlotState(caster, casterCaster, target, targetCaster, skillObject, skill, castTlId);
        casterUnit.ActivePlotState = state;
        skill.ActivePlotState = state;

        if (PlotEndRules.ShouldEndWithoutTree(Tree))
        {
            // 67 plots in 10.0.2.13 have no position-1 plot_events row, so PlotManager built no tree for
            // them while 30 skills still cast them (13499 → 47, 16728-16745 → 283-300, ...). Every call
            // site runs this from Task.Run, so the null-tree dereference was swallowed as an unobserved
            // task exception and the skill never ended. PlotManager warns about the count once at load;
            // per cast there is nothing to say.
            if (PlotEndRules.OwnsSkillEnd(skill.Template?.PlotOnly ?? false, skill.ForcePlotGraphOnly,
                    skill.PlayerAuthoredPlotCast))
            {
                // The plot owns the skill end here (13499, 36858, and a player-authored interaction cast
                // that is holding its end back), so it runs the sequence the tree itself finishes with.
                PlotTree.EndPlotWithoutTree(state);
            }
            else
            {
                // Skill.Use carries on to cast, fire and end this skill itself, so the plot only drops its
                // state. Ending it here would release the TlId and arm the cooldown from under a cast that
                // is still running - and a cast that is holding its end back for its graph is not running,
                // which is why OwnsSkillEnd above counts that case.
                PlotTree.DropPlotState(state);
            }
        }
        else
        {
            // I am guessing we want to do something here to run it in a thread, or at least using Async
            await Tree.ExecuteAsync(state);
        }

        if (casterCaster is SkillItem skillItem && caster is Character player && skillItem.SkillSourceItem != null)
        {
            // Trigger item use if not cancelled
            if (!state.CancellationRequested())
                player.ItemUse(skillItem.SkillSourceItem);
            // Free the item from lock
            player.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ItemUnlock, new ItemUpdate(skillItem.SkillSourceItem), []));
        }
    }
}
