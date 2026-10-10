using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units;

public class Transfer : Unit
{
    /// <summary>
    /// Signed offset along the motor's forward axis for the bounded child (not in client DB; tuned for default carriage pair).
    /// </summary>
    public const float BoundedChildAlongFrontOffsetMeters = -9.24417f;

    /// <summary>Steering constants for transfer models that have no vehicle_models row.</summary>
    private const float DefaultAngularVelocity = 1f;
    private const float DefaultMass = 450f;

    public override UnitTypeFlag TypeFlag { get => UnitTypeFlag.Transfer; }
    public override BaseUnitType BaseUnitType => BaseUnitType.Transfer;

    public override ModelPostureType ModelPostureType { get => ModelPostureType.TurretState; }

    //public uint Id { get; set; } // moved to BaseUnit
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public uint BondingObjId { get; set; }
    public AttachPointKind AttachPointId { get; set; }
    public TransferTemplate Template { get; set; }
    public Transfer Bounded { get; set; }
    public TransferSpawner Spawner { get; set; }
    public List<Doodad> AttachedDoodads { get; set; } = [];
    public List<Character> AttachedCharacters { get; set; } = [];
    public DateTime SpawnTime { get; set; }
    public float RotationDegrees { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 AngVel { get; set; }
    public int Steering { get; set; } = 1;
    public sbyte Throttle { get; set; } // ?
    public int PathPointIndex { get; set; }
    public float Speed { get; set; }
    public float RotSpeed { get; set; }  // ?
    public Quaternion Rot { get; set; }
    public bool Reverse { get; set; }
    //public sbyte RequestSteering { get; set; }
    //public sbyte RequestThrottle { get; set; }
    public DateTime WaitTime { get; set; }
    public uint TimeLeft => WaitTime > DateTime.UtcNow ? (uint)(WaitTime - DateTime.UtcNow).TotalMilliseconds : 0;

    #region Attributes

    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var result = formula.Evaluate(parameters);
            var res = (int)result;
            foreach (var bonus in GetBonuses(UnitAttribute.Str))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public int Dex
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Dex);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Dex))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public int Sta
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Sta);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Sta))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public int Int
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Int);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Int))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public int Spi
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Spi);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Spi))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public int Fai
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Fai);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Fai))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public override int MaxHp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.MaxHealth);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return ClampToLimit(res, UnitAttribute.MaxHealth);
        }
    }

    public override int HpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.HealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            res += Spi / 10;
            foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public override int PersistentHpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            res /= 5; // TODO ...
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public override int MaxMp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.MaxMana);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return ClampToLimit(res, UnitAttribute.MaxMana);
        }
    }

    public override int MpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.ManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            res += Spi / 10;
            foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    public override int PersistentMpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            res /= 5; // TODO ...
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                {
                    res += (int)(res * bonus.Value / 100f);
                }
                else
                {
                    res += (int)bonus.Value;
                }
            }

            return res;
        }
    }

    #endregion

    public override void Spawn()
    {
        ParentWorld.AddObject(this);
        Show();
    }

    public override void AddVisibleObject(Character character)
    {
        IsVisible = true;
        // spawn the cabin
        character.SendPacket(new SCUnitStatePacket(this));

        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    public PacketStream WriteTelescopeUnit(PacketStream stream)
    {
        stream.WriteBc(ObjId);
        stream.Write(Template.Id);
        stream.WritePosition(Transform.World.Position);
        stream.Write(Name);

        return stream;
    }

    // ******************************************************************************************************************
    // Transport movement information.
    // ******************************************************************************************************************

    public Dictionary<int, List<WorldSpawnPosition>> Routes { get; set; } = []; // Steering/TransferPath: all road segments.
    public List<WorldSpawnPosition> TransferPath { get; set; } = []; // Current road segment.
    public bool MoveToPathEnabled { get; set; }    // Whether transport movement is enabled.
    public bool MoveToForward { get; set; }        // movement direction true -> forward, true -> back
    public int MoveStepIndex { get; set; }         // current checkpoint (where are we running now)
    //public float DeltaTime { get; set; } = 0.1f;
    public Vector3 vPosition { get; set; }
    public Vector3 vTarget { get; set; }
    public Vector3 vDistance { get; set; }
    public Vector3 vVelocity { get; set; }
    public float Distance { get; set; }
    public float MaxVelocityForward { get; set; } = 5.4f;
    //public float MaxVelocityBackward { get; set; } = 0.25f;
    public float RangeToCheckPoint { get; set; } = 1.5f; // distance to checkpoint at which it is considered that we have reached it
    public float VelAccel { get; set; } = 0.3f;
    public double Angle { get; set; }
    public float Mass { get; set; }
    public float MaxForce { get; set; }

    public void GoToPath(Transfer transfer)
    {
        if (transfer == null) { return; }
        if (transfer.TransferPath.Count <= 0) { return; }

        transfer.MoveToPathEnabled = true;
        transfer.MoveToForward = true;
        // Try using these values as the transport movement speed.
        // let's try to take these values as vehicle speed
        transfer.MaxVelocityForward = transfer.Template.PathSmoothing + 1.6f;
        transfer.Speed = 0;
        if (!MoveToPathEnabled || !transfer.IsInPatrol)
        {
            //Logger.Warn("the route is stopped.");
            StopMove(transfer);
            return;
        }

        // presumably the path is already registered in MovePath
        //Logger.Warn("trying to get on the road...");
        // first go to the closest checkpoint
        var i = GetMinCheckPoint(transfer, transfer.TransferPath);
        if (i < 0)
        {
            //Logger.Warn("no checkpoint found.");
            StopMove(transfer);
            return;
        }

        //Logger.Warn("found nearest checkpoint # " + i + " walk there ...");
        //Logger.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
        transfer.MoveToPathEnabled = true;
        transfer.MoveStepIndex = i;
        //Logger.Warn("checkpoint #" + i);
        //Logger.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
        var s = transfer.TransferPath[transfer.MoveStepIndex];
        transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
        // 1. Start of the route.
        if (MoveStepIndex == 0 && Steering == 0 && transfer.Template.WaitTime > 0)
        {
            var time = transfer.Template.WaitTime;
            transfer.WaitTime = DateTime.UtcNow.AddSeconds(time);
            //Logger.Info("TransfersPath #" + transfer.Template.Id);
            //Logger.Warn("path #" + Steering);
            //Logger.Warn("walk to #" + MoveStepIndex);
            //Logger.Info("pause to #" + time);
            //Logger.Warn("x:=" + transfer.Position.X + " y:=" + transfer.Position.Y + " z:=" + transfer.Position.Z);
        }
    }

    private static int GetMinCheckPoint(Transfer transfer, List<WorldSpawnPosition> pointsList)
    {
        var index = -1;
        // check for a route
        if (pointsList.Count == 0)
        {
            //Logger.Warn("no route data.");
            return -1;
        }
        float delta;
        var minDist = 0f;
        transfer.vTarget = transfer.Transform.World.ClonePosition();
        for (var i = 0; i < pointsList.Count; i++)
        {
            transfer.vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

            //Logger.Warn("#" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

            delta = MathUtil.GetDistance(transfer.vTarget, transfer.vPosition);
            if (delta > 200) { continue; } // Find a point that is not too far from the carriage.

            if (index == -1) // first assignment
            {
                index = i;
                minDist = delta;
            }
            if (delta < minDist) // save if less
            {
                index = i;
                minDist = delta;
            }
        }

        return index;
    }

    private static void StopMove(Transfer transfer)
    {
        //Logger.Warn("stop moving ...");
        transfer.Throttle = 0;
        transfer.Speed = 0;
        transfer.RotSpeed = 0;
        transfer.Velocity = Vector3.Zero;
        transfer.vVelocity = Vector3.Zero;
    }

    private static Vector3 Truncate(Vector3 steering, float maxForce)
    {
        if (steering.Length() > maxForce)
        {
            return Vector3.Normalize(steering) * maxForce;
        }
        return steering;
    }

    /// <summary>
    /// Moves the Transfer to it's next position
    /// </summary>
    /// <param name="transfer"></param>
    public void MoveTo(Transfer transfer)
    {
        if (transfer.TimeLeft > 0) { return; } // Pause at the beginning/end of a route and at stops.

        if (!transfer.MoveToPathEnabled || !transfer.IsInPatrol)
        {
            StopMove(transfer);
            return;
        }

        /* Seek behavior */
        /* Behaviors: Seek */
        // https://code.tutsplus.com/understanding-steering-behaviors-flee-and-arrival--gamedev-1303t?_ga=2.235770881.1818027478.1708218508-301354123.1708218508
        // Most transfer models have no vehicle_models row — carriage 654, airship 657 and lift 2094
        // are all absent — so a missing model must not stop the route. It only contributes steering
        // constants, which fall back to values that keep the seek behaviour stable.
        var vehicleModel = ModelManager.Instance.GetVehicleModels(transfer.Template.ModelId);

        transfer.vPosition = transfer.Transform.World.ClonePosition();
        // 10.0.2.13: vehicle_models.velocity is gone, and wheeled_vehicle_speed_limit only exists for
        // the 76 models that have a row. transfers.velocity is per-transfer and always present
        // (carriage 8.0, airship 9.6, lift 8.0), so it drives the route directly.
        MaxVelocityForward = transfer.Template.Velocity;
        MaxForce = vehicleModel?.AngVel ?? DefaultAngularVelocity;
        Mass = vehicleModel?.WheeledVehicleMass ?? DefaultMass;
        //var slowingRadius = transfer.Template.PathSmoothing; // Distance at which deceleration begins.

        // vehicleModel is null for every transfer model without a vehicle_models row, which is most
        // of them — fall through to the default gear ratio rather than dereferencing it.
        transfer.VelAccel = vehicleModel?.WheeledVehicleMaxGear switch
        {
            1 => vehicleModel.WheeledVehicleGearSpeedRatio1,
            2 => vehicleModel.WheeledVehicleGearSpeedRatio2,
            3 => vehicleModel.WheeledVehicleGearSpeedRatio3,
            _ => 0.3f
        };

        // Direction vector to the target (argument order matters for facing the target).
        // vector direction to the target (the sequence of arguments is important to look at the target)
        transfer.vDistance = transfer.vTarget - transfer.vPosition;

        // distance to the point where we are moving
        transfer.Distance = transfer.vDistance.Length();
        var nextPoint = transfer.Distance < transfer.RangeToCheckPoint;
        if (nextPoint)
        {
            transfer.vPosition = transfer.vTarget;
            // get next path or point # in current path
            transfer.NextPoint(transfer);
            return;
        }

        transfer.vVelocity = Vector3.Normalize(transfer.vDistance) * transfer.VelAccel;
        var desiredVelocity = Vector3.Normalize(transfer.vDistance) * MaxVelocityForward;
        var steering = desiredVelocity - transfer.vVelocity;
        steering = Truncate(steering, MaxForce);
        steering /= Mass;
        transfer.vVelocity = Truncate(transfer.vVelocity + steering, MaxVelocityForward);
        transfer.vPosition += transfer.vVelocity;

        // The following Speed, Velocity, and AngVel calculations rotate wheels and airship propellers.
        // the following calculations Speed, Velocity and AngVel allow the wheels and propellers on airships to rotate
        transfer.Speed = MaxVelocityForward;
        transfer.Velocity = transfer.vVelocity * 5;

        transfer.Angle = MathUtil.CalculateDirection(transfer.vPosition, transfer.vTarget);
        if (transfer.Reverse)
        {
            transfer.Angle += MathF.PI;
            //transfer.velAccel = vehicleModel.WheeledVehicleGearSpeedRatioReverse; // Reverse gear.
            transfer.AngVel = new Vector3(MaxVelocityForward, 0, 0);
        }
        else
        {
            transfer.AngVel = new Vector3(-MaxVelocityForward, 0, 0);
        }
        var rot = MathUtil.ConvertRadianToDirectionShort(transfer.Angle);
        transfer.Transform.World.SetZRotation((float)Angle);
        transfer.Rot = new Quaternion(rot.X, rot.Z, rot.Y, rot.W);

        // update TransfersPath variable
        var moveTypeTr = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
        moveTypeTr.UseTransferBase(transfer);

        // Only send movement of the main vehicle motor, client will drag carriage on its own
        if (transfer.Bounded is not null || transfer.ParentObj is null)
        {
            transfer.BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveTypeTr), false);
        }

        // Added so whatever riding this, doesn't clip out of existence when moving
        transfer.Transform.FinalizeTransform();
        transfer.SetPosition(transfer.vPosition.X, transfer.vPosition.Y, transfer.vPosition.Z, 0, 0, (float)transfer.Angle);
        transfer.Transform.World.SetPosition(transfer.vPosition, new Vector3(0, 0, (float)transfer.Angle));

        // Keep bound carriage parts in sync server-side.
        // The client may initially render them attached at the station, but without server-side position updates
        // they can later "snap back" to the spawn point when the streaming/visibility refreshes.
        if (transfer.Bounded is not null)
        {
            var child = transfer.Bounded;
            var (cx, cy) = MathUtil.AddDistanceToFront(BoundedChildAlongFrontOffsetMeters,
                transfer.vPosition.X,
                transfer.vPosition.Y,
                (float)transfer.Angle);
            var cz = transfer.vPosition.Z;

            child.SetPosition(cx, cy, cz, 0, 0, (float)transfer.Angle);
            child.Transform.World.SetPosition(new Vector3(cx, cy, cz), new Vector3(0, 0, (float)transfer.Angle));
            child.Rot = transfer.Rot;

            var childMove = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
            childMove.UseTransferBase(child);
            child.BroadcastPacket(new SCOneUnitMovementPacket(child.ObjId, childMove), false);
        }

        // Follow the region as the route crosses one. Movement alone never re-registers visibility,
        // so the rig stayed bound to the region it spawned in and streamed out of every client the
        // moment it drove clear of that region's neighbourhood. AddVisibleObject resolves through
        // the transform root (the bounded child, moved just above) and early-returns when the
        // region is unchanged, so this is cheap on the common tick.
        Core.Managers.World.WorldManager.Instance.AddVisibleObject(transfer);
    }

    private void NextPoint(Transfer transfer)
    {
        double time;

        if (!transfer.MoveToPathEnabled)
        {
            //Logger.Warn("OnMove disabled");
            StopMove(transfer);
            return;
        }

        if (transfer.TransferPath.Count <= 0) { return; }

        if (transfer.Template.Cyclic)
        {

            //var s = transfer.TransferPath[transfer.MoveStepIndex];
            //vTarget = new Vector3(s.X, s.Y, s.Z);
            //Logger.Info("TransfersPath #" + transfer.Template.Id);
            //Logger.Warn("path #" + Steering);
            //Logger.Warn("walk to #" + MoveStepIndex);
            //Logger.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
            // Iterate through each route segment in order, starting at the beginning of each one.
            if (transfer.MoveStepIndex >= transfer.TransferPath.Count - 1)
            {
                // This route segment has ended.
                transfer.MoveStepIndex = 0;
                if (transfer.Steering >= transfer.Routes.Count - 1)
                {
                    // All road segments have ended; start over.
                    //Logger.Warn("we are at the end point.");
                    transfer.Steering = 0; // Select the first route segment.
                    transfer.TransferPath = transfer.Routes[transfer.Steering];
                    var s = transfer.TransferPath[transfer.MoveStepIndex];
                    transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
                }
                else
                {
                    // Check for stops along the route.
                    //Logger.Warn("we reached checkpoint go further...");
                    // Continue along the route.
                    transfer.Steering++; // Select the next route segment.
                    transfer.TransferPath = transfer.Routes[transfer.Steering];
                    var s = transfer.TransferPath[transfer.MoveStepIndex];
                    transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
                    // Pause here at the end of the segment if the database defines one.
                    if (transfer.Steering != 0 && (transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd > 0 || transfer.Template.TransferAllPaths[Steering].WaitTimeStart > 0))
                    {
                        time = transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd > 0
                            ? transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd
                            : transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart;
                        transfer.WaitTime = DateTime.UtcNow.AddSeconds(time);
                        StopMove(transfer);
                    }
                }
            }
            else
            {
                // Pause here at the start of the segment if the database defines one.
                if (transfer.MoveStepIndex == 0 && transfer.Steering == 0 && (transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0 || transfer.Template.WaitTime > 0))
                {
                    time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0
                        ? transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart
                        : transfer.Template.WaitTime;
                    transfer.WaitTime = DateTime.UtcNow.AddSeconds(time);
                    StopMove(transfer);
                }

                // The route is not finished; keep moving.
                transfer.MoveStepIndex++;
                var s = transfer.TransferPath[transfer.MoveStepIndex];
                transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
            }
        }
        else
        {
            // There is only one segment; travel forward first, then backward.
            transfer.Steering = 0; // Always zero for a single segment.
            //var s = TransferPath[MoveStepIndex];
            // vTarget = new Vector3(s.X, s.Y, s.Z);
            // The full route has ended; return in the opposite direction.
            if (transfer.MoveStepIndex >= transfer.TransferPath.Count - 1)
            {
                //Logger.Warn("we are at the end point.");
                transfer.Reverse = true;
                // Pause here at the start of the segment.
                time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0
                    ? transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart
                    : transfer.Template.WaitTime;
                transfer.WaitTime = DateTime.UtcNow.AddSeconds(time);
                StopMove(transfer);
            }
            // At the starting point, move forward.
            if (transfer.MoveStepIndex == 0)
            {
                //Logger.Warn("we are at the begin point.");
                transfer.Reverse = false;
                // Pause here at the end of the segment.
                time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeEnd > 0
                    ? transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeEnd
                    : transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart;
                transfer.WaitTime = DateTime.UtcNow.AddSeconds(time);
            }

            if (transfer.Reverse)
            {
                // Move backward along the same route.
                transfer.MoveStepIndex--;
                //TransferPath = Routes[Steering];
                var s = transfer.TransferPath[transfer.MoveStepIndex];
                transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
            }
            else
            {
                // Continue along the route.
                transfer.MoveStepIndex++;
                //TransferPath = Routes[Steering];
                var s = transfer.TransferPath[transfer.MoveStepIndex];
                vTarget = new Vector3(s.X, s.Y, s.Z);
            }
        }
    }
}
