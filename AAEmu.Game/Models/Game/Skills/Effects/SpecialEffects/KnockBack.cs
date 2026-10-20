using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class KnockBack : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.KnockBack;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is Character)
            Logger.Debug("Special effects: KnockBack value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (target is not Unit trg || trg.Hp <= 0)
            return;

        // KnockBack values use millimeters throughout the database, including small values.
        // Treating values <= 100 as meters made a 100-unit knockback move a target 100 meters.
        const float MillimetersPerMeter = 1000f;
        var distance = value1 / MillimetersPerMeter;
        if (distance <= 0f)
            distance = 1f;

        var from = caster.Transform.World.Position;
        var to = trg.Transform.World.Position;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
        {
            // Degenerate: push along caster facing.
            var (px, py) = MathUtil.AddDistanceToFront(distance, to.X, to.Y, caster.Transform.World.Rotation.Z);
            dx = px - to.X;
            dy = py - to.Y;
            len = MathF.Sqrt(dx * dx + dy * dy);
        }

        var nx = dx / len;
        var ny = dy / len;
        var zLift = value3 / MillimetersPerMeter;
        var destX = to.X + nx * distance;
        var destY = to.Y + ny * distance;
        var destZ = to.Z + MathF.Max(0f, zLift);

        trg.BroadcastPacket(new SCKnockBackUnitPacket(trg.ObjId, destX, destY, destZ), true);

        // Players are World-owned for movement; NPCs under ZoneAuthority get position from Zone.
        if (trg is Character || !WorldIntegration.ZoneAuthority)
        {
            var transform = trg.Transform;
            if (transform.Parent != null)
            {
                // Knockback destinations are calculated in world coordinates, while SetPosition writes
                // Transform.Local. A mounted character is parented to its mate, so writing the world
                // destination directly as local coordinates adds the mount position a second time.
                var localPosition = transform.GetLocalFromWorld(destX, destY, destZ);
                var localRotation = transform.Local.Rotation;
                trg.SetPosition(localPosition.X, localPosition.Y, localPosition.Z,
                    localRotation.X, localRotation.Y, localRotation.Z);
            }
            else
            {
                var worldRotation = transform.World.Rotation;
                trg.SetPosition(destX, destY, destZ,
                    worldRotation.X, worldRotation.Y, worldRotation.Z);
            }
        }

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayKnockBackToZone?.Invoke(trg.ObjId, destX, destY, destZ);

        if (trg is Character)
        {
            var final = trg.Transform.World.Position;
            Logger.Info(
                "Knockback result skill={0} caster={1}({2}/{3}) target={4}({5}) zone={6} raw=({7},{8},{9},{10}) distance={11:F3}m zLift={12:F3}m from=({13:F1},{14:F1},{15:F1}) targetBefore=({16:F1},{17:F1},{18:F1}) destination=({19:F1},{20:F1},{21:F1}) targetAfter=({22:F1},{23:F1},{24:F1}) parented={25} zoneAuthority={26}",
                skill.Id, caster.Name, caster.ObjId, caster.GetType().Name,
                trg.Name, trg.ObjId, trg.Transform.ZoneId,
                value1, value2, value3, value4, distance, zLift,
                from.X, from.Y, from.Z, to.X, to.Y, to.Z,
                destX, destY, destZ, final.X, final.Y, final.Z,
                trg.Transform.Parent != null, WorldIntegration.ZoneAuthority);
        }
    }
}
