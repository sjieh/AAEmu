using System.Drawing;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadRemovesSubCommand : SubCommandBase
{
    public DoodadRemovesSubCommand()
    {
        Title = "[Doodad Removes]";
        Description = "Remove the nearest Doodads around a character within a <r> radius";
        CallPrefix = $"{CommandManager.CommandPrefix}removes";
        //AddParameter(new StringSubCommandParameter("radius", "radius", true, "r"));
        AddParameter(new NumericSubCommandParameter<uint>("Radius", "radius", true));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Run the method in a separate task (thread).
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("Radius", out var radius))
                {
                    RemoveAll(character, radius, messageOutput);
                }
            }
            catch (Exception ex)
            {
                // Handle the exception, for example by writing it to the log.
                Logger.Error($"Ошибка при выполнении метода: {ex.Message}");
            }
        });
    }

    private void RemoveAll(ICharacter character, uint radius, IMessageOutput messageOutput)
    {
        var nearestDoodad = GetNearestDoodad(character, 5f);
        if (nearestDoodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, "Nearest Doodad does not exist");
            Logger.Warn("Nearest Doodad does not exist");
            return;
        }

        var doodadsInRadius = GetDoodadsInRadius(character, radius, nearestDoodad.TemplateId);
        foreach (var doodad in doodadsInRadius)
        {
            // Remove Doodad
            doodad.Spawner.Id = 0xffffffff; // Removed manually; /save all should not write this entry to doodad_spawns_new.json.
            doodad.Hide();
            SendMessage(messageOutput, $"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
            Logger.Warn($"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
        }
    }

    private static Doodad GetNearestDoodad(ICharacter character, float radius)
    {
        // Get the list of doodad objects.
        var doodads = WorldManager.GetAround<Doodad>((GameObject)character, radius);

        // Initialize variables for the nearest object and its distance.
        Doodad nearestDoodad = null;
        var minDistance = float.MaxValue;

        // Iterate through the objects.
        foreach (var doodad in doodads)
        {
            // Calculate the distance between the character and the current doodad.
            var distance = Vector3.Distance(character.Transform.World.Position, doodad.Transform.World.Position);

            // If this distance is shorter, update the nearest object and the minimum distance.
            if (!(distance < minDistance))
            {
                continue;
            }

            nearestDoodad = doodad;
            minDistance = distance;
        }

        return nearestDoodad;
    }

    private static List<Doodad> GetDoodadsInRadius(ICharacter character, float radius, uint templateId)
    {
        // Get the list of doodad objects.
        var doodads = WorldManager.GetAround<Doodad>((GameObject)character, radius);

        // Keep only objects within range whose ObjId matches the TemplateId.
        var doodadsInRadius = doodads
            .Where(doodad => Vector3.Distance(character.Transform.World.Position, doodad.Transform.World.Position) <= radius && doodad.TemplateId == templateId)
            .ToList();

        return doodadsInRadius;
    }
}
