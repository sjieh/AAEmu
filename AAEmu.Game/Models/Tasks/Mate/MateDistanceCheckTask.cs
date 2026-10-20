using AAEmu.Game.Core.Managers.World;
using MateUnit = AAEmu.Game.Models.Game.Units.Mate;
using NLog;

namespace AAEmu.Game.Models.Tasks.Mate;

/// <summary>
/// Removes an active mate that has become too far from its owner. The 512 metre horizontal range
/// matches the Zone Manager's default distance for prestarting a neighboring zone.
/// </summary>
public sealed class MateDistanceCheckTask(MateUnit mate) : Task
{
    public const float MaximumDistanceMeters = 512f;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const float MaximumDistanceSquared = MaximumDistanceMeters * MaximumDistanceMeters;

    public override void Execute()
    {
        if (Cancelled || mate == null)
            return;

        var world = mate.ParentWorld;
        if (world == null || world.IsDisposed || world.MateManager == null)
            return;

        // Portal transitions move the same mate instance between world instances. Skip the brief
        // handoff window, and never compare coordinates from different instance coordinate spaces.
        var owner = WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId);
        if (owner?.ParentWorld == null || !ReferenceEquals(owner.ParentWorld, world))
            return;

        // A mate carrying anyone must not be removed by the owner-distance cleanup. In
        // particular, the owner is normally a transform child of a mounted mate, so a large
        // position discrepancy during a zone handoff is a synchronization problem, not a reason
        // to delete the mount and forcibly dismount its rider. Check both the seat records and
        // the transform relationship because they can be briefly out of sync during transitions.
        var hasPassengers = mate.Passengers.Values.Any(passenger => passenger._objId != 0);
        var ownerIsAttached = ReferenceEquals(owner.Transform.Parent, mate.Transform);
        if (hasPassengers || ownerIsAttached)
            return;

        var ownerPosition = owner.Transform.World.Position;
        var matePosition = mate.Transform.World.Position;
        var dx = ownerPosition.X - matePosition.X;
        var dy = ownerPosition.Y - matePosition.Y;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared <= MaximumDistanceSquared)
            return;

        var distance = MathF.Sqrt(distanceSquared);
        Logger.Warn(
            "Despawned mate {0} (tlId {1}) for owner {2} ({3}): separation {4:0.0}m exceeded {5:0}m; " +
            "owner zone={6} pos=({7:0.0},{8:0.0},{9:0.0}), mate zone={10} pos=({11:0.0},{12:0.0},{13:0.0})",
            mate.ObjId, mate.TlId, owner.Name, owner.ObjId, distance, MaximumDistanceMeters,
            owner.Transform.ZoneId, ownerPosition.X, ownerPosition.Y, ownerPosition.Z,
            mate.Transform.ZoneId, matePosition.X, matePosition.Y, matePosition.Z);
        owner.Mates.DespawnMate(mate.TlId);
    }
}
