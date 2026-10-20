using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class MateManager(WorldInstance parentWorldInstance)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Regex _nameRegex;

    private Dictionary<uint, List<Mate>> _activeMates = []; // ownerId, Mount

    private WorldInstance World { get; init; } = parentWorldInstance;

    /// <summary>
    /// Gets active pets
    /// </summary>
    /// <param name="ownerId"></param>
    /// <returns></returns>
    public List<Mate> GetActiveMates(uint ownerId)
    {
        return _activeMates.GetValueOrDefault(ownerId) ?? [];
    }

    /// <summary>Moves the owner's active pets beside them after a portal landing.</summary>
    public void MoveActiveMatesWithOwner(
        Character owner,
        WorldInstance destinationWorld,
        float ownerX,
        float ownerY,
        float ownerZ,
        float ownerYaw)
    {
        if (owner == null || destinationWorld == null)
            return;

        var mates = GetActiveMates(owner.Id).ToList();
        if (mates.Count == 0)
            return;

        // Keep the pet beside the player without placing it across a zone boundary from the portal
        // landing. The destination zone is already loaded for the character; an adjacent zone may not be.
        var (spawnX, spawnY) = MathUtil.AddDistanceToFront(2f, ownerX, ownerY, ownerYaw);
        var ownerZoneId = owner.Transform.ZoneId;
        var candidateZoneId = WorldManager.Instance.GetZoneId(destinationWorld.Template, spawnX, spawnY);
        if (candidateZoneId == 0 || candidateZoneId != ownerZoneId)
        {
            spawnX = ownerX;
            spawnY = ownerY;
        }

        foreach (var mate in mates)
        {
            if (mate == null)
                continue;

            var changingWorld = !ReferenceEquals(World, destinationWorld);
            if (changingWorld)
            {
                // A different player may be occupying another seat. Keep that rider and mount together
                // in the source world instead of moving only the mount out from under them.
                var hasOtherPassengers = mate.Passengers.Values.Any(passenger =>
                    passenger._objId != 0 && passenger._objId != owner.ObjId);
                if (hasOtherPassengers)
                {
                    Logger.Warn(
                        "Portal did not move mate {0} to world {1}: it still has passengers",
                        mate.ObjId, destinationWorld.Id);
                    continue;
                }

                // Seat records are authoritative for normal mounts, but also check the transform
                // tree so a stale or otherwise untracked passenger is not carried into another world.
                if (mate.Transform.Children.Count > 0)
                {
                    Logger.Warn(
                        "Portal did not move mate {0} to world {1}: it still has attached objects",
                        mate.ObjId, destinationWorld.Id);
                    continue;
                }

                var destinationMateManager = destinationWorld.MateManager;
                if (destinationMateManager == null ||
                    destinationMateManager.GetActiveMates(owner.Id).Any(existing => !ReferenceEquals(existing, mate)))
                {
                    Logger.Warn(
                        "Portal did not move mate {0} to world {1}: owner already has an active mate there",
                        mate.ObjId, destinationWorld.Id);
                    continue;
                }
            }

            mate.StopOrderedAttack();
            WithdrawMateFromZone(mate);
            mate.Hide();

            // A mate can be parented by an attachment or other transform. The portal destination
            // is in world coordinates, so clear any old parent before assigning the landing point.
            mate.Transform.Parent = null;

            if (changingWorld)
            {
                World.RemoveObject(mate);
                RemoveActiveMateReference(owner.Id, mate);
                mate.Transform.InstanceId = destinationWorld.Id;

                var destinationMates = destinationWorld.MateManager._activeMates;
                if (!destinationMates.TryGetValue(owner.Id, out var destinationMateList))
                {
                    destinationMateList = [];
                    destinationMates.Add(owner.Id, destinationMateList);
                }
                destinationMateList.Add(mate);
            }

            mate.Transform.Local.SetPosition(spawnX, spawnY, ownerZ, 0f, 0f, ownerYaw);

            if (changingWorld)
                mate.Spawn();
            else
                mate.Show();

            AnnounceMateToZone(mate);
            Logger.Info(
                "Moved active mate {0} with owner {1} through portal to world {2} at ({3:0.0}, {4:0.0}, {5:0.0})",
                mate.ObjId, owner.Id, destinationWorld.Id, spawnX, spawnY, ownerZ);
        }
    }

    private void RemoveActiveMateReference(uint ownerId, Mate mate)
    {
        if (!_activeMates.TryGetValue(ownerId, out var activeMateList))
            return;

        activeMateList.Remove(mate);
        if (activeMateList.Count == 0)
            _activeMates.Remove(ownerId);
    }

    /// <summary>
    /// Gets an active pet by it's TlId
    /// </summary>
    /// <param name="tlId"></param>
    /// <returns></returns>
    public Mate GetActiveMateByTlId(uint tlId)
    {
        return _activeMates.Values.SelectMany(mateList => mateList).FirstOrDefault(mate => mate.TlId == tlId);
    }

    /// <summary>
    /// Gets an active pet by it's ObjId
    /// </summary>
    /// <param name="mateObjId"></param>
    /// <returns></returns>
    public Mate GetActiveMateByMateObjId(uint mateObjId)
    {
        return _activeMates.Values.SelectMany(mateList => mateList).FirstOrDefault(mate => mate.ObjId == mateObjId);
    }

    /// <summary>
    /// Checks if a ObjId is mounted on any of the pets and returns which seat
    /// </summary>
    /// <param name="objId"></param>
    /// <param name="attachPoint"></param>
    /// <returns></returns>
    public Mate GetIsMounted(uint objId, out AttachPointKind attachPoint)
    {
        attachPoint = AttachPointKind.System;
        foreach (var mate in _activeMates.Values.SelectMany(mateList => mateList))
            foreach (var ati in mate.Passengers.Where(ati => ati.Value._objId == objId))
            {
                attachPoint = ati.Key;
                return mate;
            }

        return null;
    }

    /// <summary>
    /// Change the state of a pet
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="newState"></param>
    public void ChangeStateMate(GameConnection connection, uint tlId, byte newState)
    {
        var owner = connection.ActiveChar;
        var mateInfoList = GetActiveMates(owner.Id);
        foreach (var mateInfo in mateInfoList)
        {
            if (mateInfo?.TlId != tlId) continue;
            // enum_mate_state / MateState: 1 passive, 2 protective, 3 aggressive (compact.sqlite3).
            mateInfo.UserState = newState;
            if (newState == (byte)MateState.Passive)
                mateInfo.StopOrderedAttack();
            else if (newState == (byte)MateState.Aggressive && mateInfo.CurrentTarget != null)
                mateInfo.StartOrderedAttack();
        }
    }

    /// <summary>
    /// Changes the current target of a pet 
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="objId"></param>
    public void ChangeTargetMate(GameConnection connection, short tlId, uint objId)
    {
        var owner = connection.ActiveChar;
        var mateInfo = tlId >= 0 ? GetActiveMateByTlId((ushort)tlId) : null;
        if (owner == null || mateInfo == null || mateInfo.OwnerObjId != owner.ObjId)
        {
            Logger.Warn(
                "Rejected mate target change tlId={0} from {1} ({2})",
                tlId, owner?.Name ?? "<disconnected>", owner?.ObjId ?? 0);
            return;
        }

        var target = objId > 0 ? World.GetUnit(objId) : null;
        if (objId != 0 && target == null)
        {
            Logger.Warn(
                "Rejected missing mate target {0} for mate {1} from {2}",
                objId, mateInfo.ObjId, owner.Name);
            return;
        }

        mateInfo.CurrentTarget = target;
        mateInfo.BroadcastPacket(
            new SCTargetChangedPacket(mateInfo.ObjId, mateInfo.CurrentTarget?.ObjId ?? 0), true);

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayTargetChangedToZone?.Invoke(
                mateInfo.ObjId, mateInfo.CurrentTarget?.ObjId ?? 0, false);

        // Start or stop the server-paced battle-mate attack when its target changes.
        if (target == null)
            mateInfo.StopOrderedAttack();
        else
            mateInfo.StartOrderedAttack();

        Logger.Info(
            "ChangeTargetMate tlId={0} mate={1} target={2} state={3}",
            mateInfo.TlId, mateInfo.ObjId, objId, mateInfo.UserState);
    }

    /// <summary>
    /// Renames a pet
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="newName"></param>
    /// <returns></returns>
    public Mate RenameMount(GameConnection connection, uint tlId, string newName)
    {
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null || mateInfo.OwnerObjId != connection.ActiveChar.ObjId)
            return null;
        if (string.IsNullOrWhiteSpace(newName) || newName.Length == 0 || !_nameRegex.IsMatch(newName)) return null;
        mateInfo.Name = newName.NormalizeName();
        mateInfo.BroadcastPacket(new SCUnitNameChangedPacket(mateInfo.ObjId, newName), false);
        return mateInfo;
    }

    /// <summary>
    /// Mounts the active character of a connection on target mount by its TlId
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="attachPoint"></param>
    /// <param name="reason"></param>
    public void MountMate(GameConnection connection, uint tlId, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        var character = connection.ActiveChar;
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null || mateInfo.IsDead) return;

        // Request seat position
        if (mateInfo.Passengers.TryGetValue(attachPoint, out var seatInfo))
        {
            // If first seat, check if it's the owner
            if (attachPoint == AttachPointKind.Driver && mateInfo.OwnerObjId != character.ObjId)
            {
                Logger.Warn($"MountMate. Non-owner {character.Name} ({character.ObjId}) tried to take the first seat on mount {mateInfo.Name} ({mateInfo.ObjId})");
                return;
            }

            // Check if seat is empty
            if (seatInfo._objId == 0)
            {
                character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPoint, reason, mateInfo.ObjId), true);
                seatInfo._objId = character.ObjId;
                seatInfo._reason = reason;

                character.Transform.Parent = mateInfo.Transform;
                character.Transform.Local.SetPosition(0, 0, 0); // correct the position of the character
                character.IsRiding = true;
                character.AttachedPoint = attachPoint;

                character.IsVisible = true; // When we're on a horse, you can see us
                // Announce the mate to the zone before attaching its rider.
                AnnounceMateToZone(mateInfo);
                WorldIntegration.RelayUnitAttachToZone?.Invoke(
                    character.ObjId, mateInfo.ObjId, (byte)attachPoint, true);
            }
        }
        else
        {
            Logger.Warn($"MountMate. Player {character.Name} ({character.ObjId}) tried to take a invalid seat {attachPoint} on mount {mateInfo.Name} ({mateInfo.ObjId})");
            return;
        }

        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Mount);
        Logger.Debug($"MountMate. mountTlId: {mateInfo.TlId}, attachPoint: {attachPoint}, reason: {reason}, seats: {string.Join(", ", mateInfo.Passengers.Values.ToList())}");
    }

    /// <summary>
    /// Unmounts a character from target mount using its TlId
    /// </summary>
    /// <param name="character"></param>
    /// <param name="tlId"></param>
    /// <param name="attachPoint"></param>
    /// <param name="reason"></param>
    public void UnMountMate(Character character, uint tlId, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null) return;

        mateInfo.StopUpdateXp();

        // Request seat position
        Character targetObj = null;
        if (mateInfo.Passengers.TryGetValue(attachPoint, out var seatInfo))
        {
            // Check if seat is taken by player
            if (seatInfo._objId != 0)
            {
                targetObj = WorldManager.Instance.GetCharacterByObjId(seatInfo._objId);
                seatInfo._objId = 0;
                seatInfo._reason = reason;
            }
        }

        if (targetObj != null)
        {
            //targetObj.Transform.StickyParent = null;
            targetObj.Transform.Parent = null;
            targetObj.SetPosition(mateInfo.Transform.World.Position.X, mateInfo.Transform.World.Position.Y, mateInfo.Transform.World.Position.Z,
                mateInfo.Transform.World.Rotation.X, mateInfo.Transform.World.Rotation.Y, mateInfo.Transform.World.Rotation.Z);
            // character.Transform = mateInfo.Transform.CloneDetached(character);
            targetObj.IsRiding = false;
            targetObj.AttachedPoint = AttachPointKind.None;

            targetObj.BroadcastPacket(new SCUnitDetachedPacket(targetObj.ObjId, reason), true);
            WorldIntegration.RelayUnitAttachToZone?.Invoke(
                targetObj.ObjId, mateInfo.ObjId, (byte)attachPoint, false);

            targetObj.Events.OnUnmount(character, new OnUnmountArgs());

            // remove_on_unmount_attach_point_id narrows remove_on_unmount to one enum_attach_point seat
            // (719 of the 771 buffs carrying a point name 1 Driver), so the seat being left is passed
            // along: a buff that names another seat stays, one that names none is removed as before.
            mateInfo.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount, (uint)attachPoint);
            targetObj.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount, (uint)attachPoint);
            Logger.Debug($"UnMountMate. mountTlId: {mateInfo.TlId}, targetObjId: {targetObj.ObjId}, attachPoint: {attachPoint}, reason: {reason}");
        }
        else
        {
            Logger.Debug($"UnMountMate. No valid seat entry, mountTlId: {mateInfo.TlId}, characterObjId: {0}, attachPoint: {attachPoint}, reason: {reason}");
        }
    }

    /// <summary>
    /// Adds a new pet (or despawns the previous one)
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="mate"></param>
    /// <param name="item"></param>
    public void AddActiveMateAndSpawn(Character owner, Mate mate, Item item)
    {
        // Get or set entry for player objId
        if (!_activeMates.TryGetValue(owner.Id, out var activeMateList))
        {
            activeMateList = [];
            _activeMates.Add(owner.Id, activeMateList);
        }

        // TODO: For later versions, allow multiple pets if they are different types (mount, battle, skill)
        foreach (var mateInfo in activeMateList)
        {
            owner.Mates.DespawnMate(mateInfo.TlId);
            return;
        }

        activeMateList.Add(mate);

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.UpdateSummonMateItem, [new ItemUpdate(item)], [])); // TODO - maybe update details
        owner.SendPacket(new SCMateSpawnedPacket(mate));

        // Re-send the owner's cooldowns with the fresh mate. The client learns a skill's cooldown by
        // starting its own countdown when a cast succeeds, and that state is rebuilt with the mount bar;
        // after a despawn/respawn it therefore has no cooldown entry, so a server refusal
        // (SkillResult.CooldownTime) has nothing to paint and "you can't use it right now" is never
        // shown even though the timer is still running on the player. Cooldowns live on the player —
        // see SkillCooldownOwnershipRules — so the player's table is what has to be re-sent.
        owner.SendPacket(new SCCooldownsPacket(owner.Cooldowns));

        Thread.Sleep(50);
        mate.Spawn();
        AnnounceMateToZone(mate);

        // Pets can stop following, or be displaced far from their owner. Check regularly so the
        // cleanup also runs when neither unit sends another movement update.
        mate.DistanceCheckTask = new Models.Tasks.Mate.MateDistanceCheckTask(mate);
        if (!TaskManager.Instance.Schedule(
                mate.DistanceCheckTask,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                -1))
        {
            mate.DistanceCheckTask = null;
            Logger.Warn("Could not schedule distance cleanup for mate {0} owned by {1}", mate.ObjId, owner.ObjId);
        }

        Logger.Debug($"Mount spawned. ownerObjId: {owner.ObjId}, tlId: {mate.TlId}, mateObjId: {mate.ObjId}");
    }

    /// <summary>Materializes a mate in its zone before attachment and movement relays.</summary>
    public static void AnnounceMateToZone(Mate mate)
    {
        if (!WorldIntegration.ZoneAuthority || mate == null)
            return;

        var zoneId = mate.Transform?.ZoneId ?? 0;
        if (zoneId == 0)
        {
            var owner = WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId);
            zoneId = owner?.Transform?.ZoneId ?? 0;
        }

        if (zoneId == 0)
            return;

        if (mate.ZoneAnnouncedTo == zoneId)
            return;

        var body = WorldIntegration.BuildWzUnitStateBody(mate);
        if (body is not { Length: > 0 })
        {
            Logger.Warn(
                "Mate zone announce refused: empty WZ state obj={0} owner={1} oldZone={2} newZone={3}",
                mate.ObjId, mate.OwnerObjId, mate.ZoneAnnouncedTo, zoneId);
            return;
        }

        var oldZoneId = mate.ZoneAnnouncedTo;
        var accepted = WorldIntegration.RelayUnitStateToZone?.Invoke(zoneId, mate.ObjId, body) ?? false;
        if (!accepted)
        {
            Logger.Warn(
                "Mate zone announce deferred: obj={0} owner={1} oldZone={2} newZone={3} pos=({4:0.0},{5:0.0},{6:0.0})",
                mate.ObjId, mate.OwnerObjId, oldZoneId, zoneId,
                mate.Transform.World.Position.X, mate.Transform.World.Position.Y, mate.Transform.World.Position.Z);
            return;
        }

        // Create the destination copy before removing the old one. A missing or dropping target
        // zone must not leave the mate without a live zone representation.
        if (oldZoneId != 0)
        {
            WorldIntegration.RelayUnitRemovedToZoneId?.Invoke(oldZoneId, mate.ObjId);
            Logger.Info("WZUnitRemoved for mate obj={0} zoneId={1} reason=zone-handoff", mate.ObjId, oldZoneId);
        }

        mate.ZoneAnnouncedTo = zoneId;
        var position = mate.Transform.World.Position;
        Logger.Info(
            "Mate zone handoff complete obj={0} owner={1} oldZone={2} newZone={3} pos=({4:0.0},{5:0.0},{6:0.0})",
            mate.ObjId, mate.OwnerObjId, oldZoneId, zoneId, position.X, position.Y, position.Z);
    }

    /// <summary>Drops the mate from the zone that currently owns it.</summary>
    public static void WithdrawMateFromZone(Mate mate)
    {
        if (mate == null || mate.ZoneAnnouncedTo == 0)
            return;

        WorldIntegration.RelayUnitRemovedToZoneId?.Invoke(mate.ZoneAnnouncedTo, mate.ObjId);
        Logger.Info("WZUnitRemoved for mate obj={0} zoneId={1}", mate.ObjId, mate.ZoneAnnouncedTo);
        mate.ZoneAnnouncedTo = 0;
    }

    /// <summary>
    /// Despawns and Removes a pet
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="tlId"></param>
    public void RemoveActiveMateAndDespawn(Character owner, uint tlId)
    {
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null)
            return; // skip if invalid tlId

        mateInfo.DistanceCheckTask?.Cancel();
        mateInfo.DistanceCheckTask = null;

        foreach (var ati in mateInfo.Passengers.ToList())
        {
            UnMountMate(WorldManager.Instance.GetCharacterByObjId(ati.Value._objId), mateInfo.TlId, ati.Key, AttachUnitReason.SlaveBinding);
        }

        mateInfo.StopOrderedAttack();
        WithdrawMateFromZone(mateInfo);

        if (_activeMates.TryGetValue(owner.Id, out var activeMateList))
        {
            activeMateList.Remove(mateInfo);
            if (activeMateList.Count == 0)
                _activeMates.Remove(owner.Id);
        }
        
        mateInfo.Delete();

        // Deferred so a re-summon cannot draw the same object id while the client still has its
        // movement-handler slot keyed to it — see MateIdReleaseTask.
        TaskManager.Instance.Schedule(
            new Models.Tasks.Mate.MateIdReleaseTask(mateInfo.ObjId, mateInfo.TlId),
            TimeSpan.FromSeconds(30));

        Logger.Debug($"Mount removed. ownerObjId: {owner.ObjId}, tlId: {mateInfo.TlId}, mateObjId: {mateInfo.ObjId}");
    }

    /// <summary>
    /// Remove all mounts that are in the world and owned by character
    /// </summary>
    /// <param name="character"></param>
    public void RemoveAndDespawnAllActiveOwnedMates(Character character)
    {
        if (character == null) return;
        foreach (var mate in GetActiveMates(character.Id))
        {
            mate.DistanceCheckTask?.Cancel();
            mate.DistanceCheckTask = null;

            foreach (var ati in mate.Passengers.ToList())
                UnMountMate(WorldManager.Instance.GetCharacterByObjId(ati.Value._objId), mate.TlId, ati.Key,
                    AttachUnitReason.SlaveBinding);

            if (mate.OwnerObjId > 0)
                character.Mates?.CaptureActiveMateState(mate);

            WithdrawMateFromZone(mate);
            mate.Delete();
            ObjectIdManager.Instance.ReleaseId(mate.ObjId);
            if (mate.TlId > 0)
                TlIdManager.Instance.ReleaseId(mate.TlId);
        }

        // _activeMates is keyed by Character.Id at every read and write; Mate.OwnerObjId is the
        // runtime object id and belongs to a different value space. Removing it here left the
        // despawned mate reachable through GetActiveMates(character.Id) after logout.
        _activeMates.Remove(character.Id);
    }

    /// <summary>
    /// Load pet related data from DB
    /// </summary>
    public void Load()
    {
        _nameRegex = new Regex(AppConfiguration.Instance.CharacterNameRegex, RegexOptions.Compiled);
        _activeMates = [];
    }
}
