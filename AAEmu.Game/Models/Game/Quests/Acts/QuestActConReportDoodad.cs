using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportDoodad(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint DoodadId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if quest was turned in at the specified Doodad
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>False</returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), DoodadId {DoodadId}");
        return questAct.OverrideObjectiveCompleted;
    }

    public override void InitializeQuest(Quest quest, QuestAct questAct)
    {
        base.InitializeQuest(quest, questAct);
        quest.Owner.Events.OnReportDoodad += questAct.OnReportDoodad;
    }

    public override void FinalizeQuest(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnReportDoodad -= questAct.OnReportDoodad;
        base.FinalizeQuest(quest, questAct);
    }

    public override void OnReportDoodad(QuestAct questAct, object sender, OnReportDoodadArgs args)
    {
        if (questAct.Id != ActId)
            return;

        var quest = questAct.QuestComponent.Parent.Parent;
        var matchesDoodad = args.DoodadId == DoodadId;
        var matchedByAlias = false;
        if (!matchesDoodad && UseAlias && QuestActObjAliasId != 0 && args.QuestId == quest.TemplateId)
        {
            // Alias-backed report rows can reference a different id than the concrete world
            // doodad. Accept that fallback only when the doodad offers this exact quest.
            matchedByAlias = DoodadManager.Instance.OffersQuest(args.DoodadId, quest.TemplateId);
            matchesDoodad = matchedByAlias;
        }

        if (!matchesDoodad)
            return;

        // This check is needed so that turning in a quest at a Doodad doesn't complete all active quests that
        // need to be turned in at the same Doodad
        var minimumProgress = questAct.Template.ParentComponent.ParentQuestTemplate.LetItDone
            ? QuestObjectiveStatus.CanEarlyComplete
            : QuestObjectiveStatus.QuestComplete;
        var isReady = quest.GetQuestObjectiveStatus() >= minimumProgress;
        // TODO: Check doodad range?

        Logger.Debug(
            $"QuestActConReportDoodad({DetailId}).OnReportDoodad: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), DoodadId {args.DoodadId}, ExpectedDoodadId {DoodadId}, AliasId {QuestActObjAliasId}, MatchedByAlias {matchedByAlias}, Selected {args.Selected}, isReady {isReady}");

        if (!isReady)
            return;

        // Same as ReportNpc: CSCompleteQuestContext carries the selective reward pick.
        quest.SelectedRewardIndex = args.Selected;
        questAct.OverrideObjectiveCompleted = true;
        if (quest.Step <= QuestComponentKind.Progress)
            quest.Step = QuestComponentKind.Ready;
        questAct.RequestEvaluation(); // Manual request since this does not use objective counters to trigger
    }
}
