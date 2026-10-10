using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Converters;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

using Newtonsoft.Json;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadSaveSubCommand : SubCommandBase
{
    private bool _isSavingInProgress;
    private readonly object _saveLock = new();
    public DoodadSaveSubCommand()
    {
        Title = "[Doodad Save]";
        Description = "Save current state of a doodad to the doodads world file.";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad save";
        AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        // Run the method in a separate task (thread).
        Task.Run(() =>
        {
            try
            {
                if (parameters.TryGetValue("ObjId", out var doodadObjId))
                    SaveById(character, doodadObjId, messageOutput);
                else
                    SaveAll(character, messageOutput);
            }
            catch (Exception ex)
            {
                // Handle the exception, for example by writing it to the log.
                Logger.Error($"Ошибка при выполнении метода: {ex.Message}");
            }
        });
    }

    private void SaveAll00(ICharacter character, IMessageOutput messageOutput)
    {
        if (_isSavingInProgress)
        {
            SendMessage(messageOutput, "Save operation is already in progress.");
            return;
        }

        lock (_saveLock)
        {
            if (_isSavingInProgress)
            {
                SendMessage(messageOutput, "Save operation is already in progress.");
                return;
            }
            _isSavingInProgress = true;
        }

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.InstanceId);
            var doodadsInWorld = WorldManager.Instance.GetAllDoodadsFromWorld(currentWorld.Id);
            var doodadSpawnersFromFile = LoadDoodadsFromFileByWorld(currentWorld);
            var doodadSpawnersToFile = new List<JsonDoodadSpawns>(doodadSpawnersFromFile);

            for (var i = 0; i < doodadSpawnersToFile.Count; i++)
            {
                doodadSpawnersToFile[i].Title = DoodadManager.GetSpawnName(doodadSpawnersToFile[i].UnitId);
                if (doodadSpawnersToFile[i].Scale == 0f)
                    doodadSpawnersToFile[i].Scale = 1f;
            }

            var addDoodads = doodadsInWorld.Where(n => n.Spawner?.Id == 0).ToList();
            var removeDoodads = doodadsInWorld.Where(n => n.Spawner?.Id == 0xffffffff).ToList();

            var allDoodads = ((Character)character).ParentWorld.GetAllDoodads();
            var lastObjId = allDoodads.Last().ObjId + 1;

            doodadsInWorld = null;
            allDoodads = null;
            GC.Collect();

            // Remove duplicates from the initial data.
            var doodadSpawnersDict = new Dictionary<(uint UnitId, float X, float Y, float Z), JsonDoodadSpawns>();
            var uniqueKeys = new HashSet<(uint, float, float, float)>();
            foreach (var spawns in doodadSpawnersToFile)
            {
                var key = (spawns.UnitId, spawns.Position.X, spawns.Position.Y, spawns.Position.Z);
                if (!uniqueKeys.Add(key))
                {
                    Logger.Warn($"Дубликат объекта с UnitId {spawns.UnitId} на позиции ({spawns.Position.X}, {spawns.Position.Y}, {spawns.Position.Z})");
                    continue;
                }
                doodadSpawnersDict.Add(key, spawns);
            }
            doodadSpawnersToFile = doodadSpawnersDict.Values.ToList(); // Replace the list with its unique entries.

            // Remove entries.
            Parallel.For(0, removeDoodads.Count, i =>
            {
                var doodad = removeDoodads[i];
                var key = (doodad.TemplateId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
                lock (doodadSpawnersToFile)
                {
                    if (doodadSpawnersDict.TryGetValue(key, out var value))
                    {
                        doodadSpawnersToFile.Remove(value);
                        doodadSpawnersDict.Remove(key);
                    }
                }
            });

            // Add new entries while checking for duplicates.
            Parallel.For(0, addDoodads.Count, i =>
            {
                var doodad = addDoodads[i];
                var pos = doodad.Transform.World;
                var key = (doodad.TemplateId, pos.Position.X, pos.Position.Y, pos.Position.Z);

                lock (doodadSpawnersToFile)
                {
                    if (doodadSpawnersDict.ContainsKey(key))
                        return;

                    var newDoodadSpawn = new JsonDoodadSpawns
                    {
                        Id = Interlocked.Increment(ref lastObjId) - 1, // Thread-safe increment.
                        UnitId = doodad.TemplateId,
                        Title = DoodadManager.GetSpawnName(doodad.TemplateId),
                        Position = new JsonPosition
                        {
                            X = pos.Position.X,
                            Y = pos.Position.Y,
                            Z = pos.Position.Z,
                            Roll = pos.Rotation.X.RadToDeg(),
                            Pitch = pos.Rotation.Y.RadToDeg(),
                            Yaw = pos.Rotation.Z.RadToDeg()
                        },
                        Scale = doodad.Scale,
                        FuncGroupId = doodad.FuncGroupId
                    };

                    doodadSpawnersToFile.Add(newDoodadSpawn);
                    doodadSpawnersDict.Add(key, newDoodadSpawn);
                }
            });

            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Template.Name, $"doodad_spawns_all_{DateTime.Now:yyyyMMdd_HHmmss}.json.add");
            var json = JsonConvert.SerializeObject(doodadSpawnersToFile, Formatting.Indented, new JsonModelsConverter());
            File.WriteAllText(jsonPathOut, json);

            stopwatch.Stop();
            var initialCount = doodadSpawnersFromFile.Count;
            var removedCount = removeDoodads.Count;
            var addedCount = addDoodads.Count;
            var finalCount = doodadSpawnersToFile.Count;

            SendMessage(messageOutput, $"All doodads have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                                       $"Initial count: {initialCount}\n" +
                                       $"Removed count: {removedCount}\n" +
                                       $"Added count: {addedCount}\n" +
                                       $"Final count: {finalCount}");

            Logger.Warn($"All doodads have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                        $"Initial count: {initialCount}\n" +
                        $"Removed count: {removedCount}\n" +
                        $"Added count: {addedCount}\n" +
                        $"Final count: {finalCount}");
        }
        finally
        {
            lock (_saveLock)
            {
                _isSavingInProgress = false;
            }
        }
    }

    private void SaveAll(ICharacter character, IMessageOutput messageOutput)
    {
        // Check whether the write should proceed.
        if (_isSavingInProgress)
        {
            SendMessage(messageOutput, "Save operation is already in progress.");
            return;
        }

        lock (_saveLock)
        {
            if (_isSavingInProgress)
            {
                SendMessage(messageOutput, "Save operation is already in progress.");
                return;
            }

            _isSavingInProgress = true;
        }

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.InstanceId);
            var doodadsInWorld = WorldManager.Instance.GetAllDoodadsFromWorld(currentWorld.Id);
            var doodadSpawnersFromFile = LoadDoodadsFromFileByWorld(currentWorld);
            var doodadSpawnersToFile = new List<JsonDoodadSpawns>(doodadSpawnersFromFile);

            for (var i = 0; i < doodadSpawnersToFile.Count; i++)
            {
                doodadSpawnersToFile[i].Title = DoodadManager.GetSpawnName(doodadSpawnersToFile[i].UnitId); // Update the title.
                if (doodadSpawnersToFile[i].Scale == 0f)
                    doodadSpawnersToFile[i].Scale = 1f; // Update the scale.
            }

            var addDoodads = doodadsInWorld.Where(n => n.Spawner?.Id == 0).ToList();
            var removeDoodads = doodadsInWorld.Where(n => n.Spawner?.Id == 0xffffffff).ToList();

            // Get all doodads and the last object ID.
            var allDoodads = ((Character)character).ParentWorld.GetAllDoodads();
            var lastObjId = allDoodads.Last().ObjId++;

            // Release memory by assigning null.
            doodadsInWorld = null;
            allDoodads = null;
            GC.Collect(); // Trigger garbage collection.

            // Create a dictionary for quickly looking up entries in doodadSpawnersToFile.
            var doodadSpawnersDict = new Dictionary<(uint UnitId, float X, float Y, float Z), JsonDoodadSpawns>();
            var uniqueKeys = new HashSet<(uint, float, float, float)>();

            foreach (var spawns in doodadSpawnersToFile)
            {
                // Build the key used for the check.
                var key = (spawns.UnitId,
                    spawns.Position.X,
                    spawns.Position.Y,
                    spawns.Position.Z);

                // Check whether the key is unique.
                if (!uniqueKeys.Add(key))
                {
                    Logger.Warn($"Дубликат объекта с UnitId {spawns.UnitId} " + $"на позиции ({spawns.Position.X}, {spawns.Position.Y}, {spawns.Position.Z})");
                    continue;
                }

                // Add the entry to the dictionary if its key is unique.
                doodadSpawnersDict.Add(key, spawns);
            }

            doodadSpawnersToFile = doodadSpawnersDict.Values.ToList(); // Replace the list with its unique entries.
            
            // Remove entries from doodadSpawnersToFile that match removeDoodads.
            Parallel.For(0, removeDoodads.Count, i =>
            {
                var doodad = removeDoodads[i];
                var key = (doodad.TemplateId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
                if (!doodadSpawnersDict.TryGetValue(key, out var value))
                    return;

                lock (doodadSpawnersToFile)
                    doodadSpawnersToFile.Remove(value);
                doodadSpawnersDict.Remove(key);
            });

            // Add entries from addDoodads to doodadSpawnersToFile.
            Parallel.For(0, addDoodads.Count, i =>
            {
                var doodad = addDoodads[i];
                var pos = doodad.Transform.World;
                var newDoodadSpawn = new JsonDoodadSpawns
                {
                    Id = lastObjId++,
                    UnitId = doodad.TemplateId,
                    Title = DoodadManager.GetSpawnName(doodad.TemplateId), // Update the title.
                    Position = new JsonPosition
                    {
                        X = pos.Position.X,
                        Y = pos.Position.Y,
                        Z = pos.Position.Z,
                        Roll = pos.Rotation.X.RadToDeg(),
                        Pitch = pos.Rotation.Y.RadToDeg(),
                        Yaw = pos.Rotation.Z.RadToDeg()
                    },
                    Scale = doodad.Scale,
                    FuncGroupId = doodad.FuncGroupId
                };

                lock (doodadSpawnersToFile)
                {
                    doodadSpawnersToFile.Add(newDoodadSpawn);
                }
            });

            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Template.Name, $"doodad_spawns_all_{DateTime.Now:yyyyMMdd_HHmmss}.json.add");

            // Write the updated data to the file.
            var json = JsonConvert.SerializeObject(doodadSpawnersToFile, Formatting.Indented, new JsonModelsConverter());
            File.WriteAllText(jsonPathOut, json);

            stopwatch.Stop();

            // Print the number of entries.
            var initialCount = doodadSpawnersFromFile.Count;
            var removedCount = removeDoodads.Count;
            var addedCount = addDoodads.Count;
            var finalCount = doodadSpawnersToFile.Count;

            SendMessage(messageOutput, $"All doodads have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                                       $"Initial count: {initialCount}\n" +
                                       $"Removed count: {removedCount}\n" +
                                       $"Added count: {addedCount}\n" +
                                       $"Final count: {finalCount}");

            Logger.Warn($"All doodads have been saved! Time taken: {stopwatch.ElapsedMilliseconds} ms\n" +
                        $"Initial count: {initialCount}\n" +
                        $"Removed count: {removedCount}\n" +
                        $"Added count: {addedCount}\n" +
                        $"Final count: {finalCount}");
        }
        finally
        {
            lock (_saveLock)
            {
                _isSavingInProgress = false;
            }
        }
    }

    private void SaveById(ICharacter character, uint doodadObjId, IMessageOutput messageOutput)
    {
        var doodad = ((Character)character).ParentWorld.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} does not exist");
            Logger.Warn($"Doodad with objId {doodadObjId} does not exist");
            return;
        }

        var world = WorldManager.Instance.GetWorld(doodad.Transform.InstanceId);
        if (world is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Could not find the worldId {doodad.Transform.WorldId}");
            Logger.Warn($"Could not find the worldId {doodad.Transform.WorldId}");
            return;
        }

        var spawn = new JsonDoodadSpawns
        {
            Id = doodad.Id,
            UnitId = doodad.TemplateId,
            Title = DoodadManager.GetSpawnName(doodad.TemplateId), // Update the title.
            Position = new JsonPosition
            {
                X = doodad.Transform.Local.Position.X,
                Y = doodad.Transform.Local.Position.Y,
                Z = doodad.Transform.Local.Position.Z,
                Roll = doodad.Transform.Local.Rotation.X.RadToDeg(),
                Pitch = doodad.Transform.Local.Rotation.Y.RadToDeg(),
                Yaw = doodad.Transform.Local.Rotation.Z.RadToDeg()
            },
            Scale = doodad.Scale,
            FuncGroupId = doodad.FuncGroupId
        };

        Dictionary<uint, JsonDoodadSpawns> spawnersFromFile = new();
        foreach (var spawnerFromFile in LoadDoodadsFromFileByWorld(world))
        {
            spawnersFromFile.TryAdd(spawnerFromFile.Id, spawnerFromFile);
        }

        spawnersFromFile[spawn.Id] = spawn;

        var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Template.Name, $"doodad_spawns_add_{DateTime.Now:yyyyMMdd_HHmmss}.json.add");
        var json = JsonConvert.SerializeObject(spawnersFromFile.Values.ToArray(), Formatting.Indented, new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        //SendDebugMessage(messageOutput, $"Doodad ObjId: {doodad.ObjId} has been saved!");
        SendMessage(messageOutput, $"All doodads have been saved with added doodad ObjId:{doodad.ObjId}, TemplateId:{doodad.TemplateId}");
        Logger.Warn($"All doodads have been saved with added doodad ObjId:{doodad.ObjId}, TemplateId:{doodad.TemplateId}");
    }

    private List<JsonDoodadSpawns> LoadDoodadsFromFileByWorld(WorldInstance worldInstance)
    {
        var worldDirectory = Path.Combine(FileManager.AppPath, "Data", "Worlds", worldInstance.Template.Name);
        var jsonFiles = Directory.GetFiles(worldDirectory, "doodad_spawns*.json");

        if (jsonFiles.Length == 0)
        {
            Logger.Warn($"No doodad_spawns*.json files found in directory {worldDirectory}.");
            return [];
        }

        var allDoodads = new List<JsonDoodadSpawns>();

        foreach (var jsonFile in jsonFiles)
        {
            var contents = FileManager.GetFileContents(jsonFile);
            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {jsonFile} is empty or could not be read.");
                continue;
            }

            Logger.Info($"Loading spawns from file {jsonFile} ...");

            var doodads = JsonHelper.DeserializeObject<List<JsonDoodadSpawns>>(contents);
            if (doodads != null)
            {
                allDoodads.AddRange(doodads);
            }
            else
            {
                Logger.Warn($"Failed to deserialize doodads from file {jsonFile}.");
            }
        }

        return allDoodads;
    }
}
