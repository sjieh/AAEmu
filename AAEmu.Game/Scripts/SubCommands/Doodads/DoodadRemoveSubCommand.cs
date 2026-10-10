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

public class DoodadRemoveSubCommand : SubCommandBase
{
    public DoodadRemoveSubCommand()
    {
        Title = "[Doodad Remove]";
        Description = "Remove using an Doodad <ObjId> or using ObjId = 0 for the nearest Doodad";
        CallPrefix = $"{CommandManager.CommandPrefix}remove";
        AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Run the method in a separate task (thread).
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("ObjId", out var doodadObjId))
                {
                    RemoveById(character, doodadObjId, messageOutput);
                }
                else
                {
                    RemoveAll(character, messageOutput);
                }
            }
            catch (Exception ex)
            {
                // Handle the exception, for example by writing it to the log.
                Logger.Error($"Ошибка при выполнении метода: {ex.Message}");
            }
        });
    }

    private void RemoveAll(ICharacter character, IMessageOutput messageOutput)
    {
        var nearestDoodad = GetNearestDoodad(character, 5f);
        if (nearestDoodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, "Nearest Doodad does not exist");
            Logger.Warn("Nearest Doodad does not exist");
            return;
        }

        var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.InstanceId);
        var doodadsInWorld = WorldManager.Instance.GetAllDoodadsFromWorld(currentWorld.Id);

        foreach (var doodad in doodadsInWorld.Where(d => d.TemplateId == nearestDoodad.TemplateId))
        {
            // Remove Doodad
            doodad.Spawner.Id = 0xffffffff; // Removed manually; /save all should not write this entry to doodad_spawns_new.json.
            doodad.Hide();
            SendMessage(messageOutput, $"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
            Logger.Warn($"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
        }
    }

    private void RemoveById(ICharacter character, uint doodadObjId, IMessageOutput messageOutput)
    {
        var doodad = ((Character)character).ParentWorld.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            doodad = GetNearestDoodad(character, 5f);
            if (doodad is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} does not exist");
                Logger.Warn($"Doodad with objId {doodadObjId} does not exist");
                return;
            }
        }

        // Remove Doodad
        doodad.Spawner.Id = 0xffffffff; // Removed manually; /save all should not write this entry to doodad_spawns_new.json.
        doodad.Hide();
        SendMessage(messageOutput, $"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
        Logger.Warn($"Doodad @DOODAD_NAME({doodad.TemplateId}), ObjId: {doodad.ObjId}, TemplateId:{doodad.TemplateId} removed successfully");
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
}
