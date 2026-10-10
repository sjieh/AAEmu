namespace AAEmu.Game.Models.Tasks.Skills;

public sealed class PendingSkillCastTask(AAEmu.Game.Models.Game.Char.Character character, long generation)
    : AAEmu.Game.Models.Tasks.Task
{
    public override void Execute() => character.ExecutePendingSkillCast(generation);
}
