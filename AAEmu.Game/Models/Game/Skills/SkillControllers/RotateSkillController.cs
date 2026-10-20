using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Handles the verified kind-7 controller profile used after Blade Flurry's leap. The plot data requests
/// a 180-degree turn after the third hit moves the character through its target; other rotate profiles
/// remain unsupported until their value semantics are known.
/// </summary>
public sealed class RotateSkillController : SkillController
{
    private const int TurnDegrees = 180;

    public RotateSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;
    }

    /// <summary>
    /// The two Blade Flurry plot rows (11966 and its duplicate 11969) both use this exact profile.
    /// Requiring the complete signature avoids guessing at the many other kind-7 rows in the database.
    /// </summary>
    public static bool Supports(SkillControllerTemplate template) =>
        template?.Value is { Length: >= 4 } values &&
        values[0] == TurnDegrees && values[1] == 0 && values[2] == 100 && values[3] == 0;

    public override void Execute()
    {
        base.Execute();
        if (Owner == null)
        {
            End();
            return;
        }

        var local = Owner.Transform.Local;
        var rotation = local.Rotation;
        local.SetRotation(rotation.X, rotation.Y, MathF.IEEERemainder(rotation.Z + MathF.PI, MathF.Tau));
        Owner.Transform.FinalizeTransform();

        // A one-shot movement state makes the server's facing agree with the plot's requested turn. The
        // client also plays the skill animation, but sending the final orientation corrects later server
        // cone/forward checks without trying to simulate the animation itself.
        var world = Owner.Transform.World;
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = world.Position.X;
        moveType.Y = world.Position.Y;
        moveType.Z = world.Position.Z;
        (moveType.RotationX, moveType.RotationY, moveType.RotationZ) = world.ToRollPitchYawSBytesMovement();
        moveType.ActorFlags = 5;
        moveType.DeltaMovement = [0, 0, 0];
        moveType.Stance = Owner.IsInBattle ? GameStanceType.Combat : GameStanceType.Relaxed;
        moveType.Alertness = Owner.IsInBattle ? MoveTypeAlertness.Combat : MoveTypeAlertness.Idle;
        moveType.Flags = Owner.IsInBattle ? MoveTypeFlags.InCombat : MoveTypeFlags.None;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds + 50;
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), true);

        End();
    }
}
