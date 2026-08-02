using Content.Shared._Crescent.Diplomacy;
using Content.Shared._Crescent.HeatSeeking;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;

namespace Content.Server._Crescent.HeatSeeking;

/// <summary>
/// This handles...
/// </summary>
public sealed class HeatSeekingSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly RotateToFaceSystem _rotate = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedShuttleSystem _shuttle = default!;

    private EntityQuery<IFFComponent> _iffQuery;

    public override void Initialize()
    {
        base.Initialize();

        _iffQuery = GetEntityQuery<IFFComponent>();

        SubscribeLocalEvent<HeatSeekingComponent, ExaminedEvent>(OnHeatSeekerExamined);
    }

    private void OnHeatSeekerExamined(Entity<HeatSeekingComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("heat-seeking-examine"));
        args.PushMarkup(Loc.GetString("heat-seeking-examine-intercept"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HeatSeekingComponent, TransformComponent>(); // get all heat seeking missiles
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!TryComp<PhysicsComponent>(uid, out var physics))
                continue;
            if (comp.StartDelay > 0f) comp.StartDelay -= frameTime;
            else if (comp.Fuel > 0f)
            {
                if (comp.Speed < comp.TopSpeed)
                    comp.Speed = physics.LinearVelocity.Length() + comp.Acceleration * frameTime;
                _physics.SetLinearVelocity(uid, _transform.GetWorldRotation(xform).ToWorldVec().Normalized() * comp.Speed);
                comp.Fuel -= frameTime;
            }
            if (comp.RefreshTicker > 0f) { comp.RefreshTicker -= frameTime; }
            else
            {
                RefreshTargetList(uid, comp, xform);
                comp.RefreshTicker = comp.RefreshRate;
            }
            if (comp.TargetEntity.HasValue) // if the missile has a target, run its guidance algorithm
            {
                if (comp.GuidanceAlgorithm == GuidanceType.PredictiveGuidance)
                    PredictiveGuidance(uid, comp, Transform(uid), frameTime);
                else
                    PurePursuit(uid, comp, Transform(uid), frameTime);
            }
        }
    }

    public void RefreshTargetList(EntityUid uid, HeatSeekingComponent comp, TransformComponent xform) // refreshes the list of potential targets
    {
        comp.TargetList.Clear();

        EntityUid? shooterGrid = null;
        if (TryComp<ProjectileComponent>(uid, out var projectile) &&
            TryComp<TransformComponent>(projectile.Shooter, out var shooterTransform))
        {
            shooterGrid = shooterTransform.GridUid;
        }

        var shipQuery = EntityQueryEnumerator<CanBeHeatTrackedComponent, TransformComponent>(); // get all entities that can be tracked
        while (shipQuery.MoveNext(out var tUid, out var tComp, out var tXform))
        {
            if (tXform.GridUid.HasValue && HasComp<IgnoreHeatSeekingTargetComponent>(tXform.GridUid.Value))
                continue;

            if (IsFriendly(shooterGrid, tXform.GridUid))
                continue;

            var angle = (
                _transform.ToMapCoordinates(tXform.Coordinates).Position -
                _transform.ToMapCoordinates(xform.Coordinates).Position
            ).ToWorldAngle(); // current angle towards target
            var distance = Vector2.Distance(
                _transform.ToMapCoordinates(xform.Coordinates).Position,
                _transform.ToMapCoordinates(tXform.Coordinates).Position
            ); // current distance from target

            float dif = (float) Math.Abs(MathHelper.RadiansToDegrees((float) angle) - MathHelper.RadiansToDegrees((float) _transform.GetWorldRotation(xform)) % 360);
            if (dif > 180)
                dif = 360 - dif;
            dif = MathHelper.DegreesToRadians(dif);

            if (dif >= MathHelper.DegreesToRadians(comp.FieldOfView))
            {
                continue;
            }
            if (distance > comp.DefaultSeekingRange)
            {
                continue;
            }

            Angle angleOffset = angle - _transform.GetWorldRotation(xform);
            float weight = distance / comp.DefaultSeekingRange - dif / 3;
            if (comp.TargetEntity == tUid)
                weight += 5f;
            weight += tComp.HeatSignature;
            comp.TargetList.Add(new SeekerTargets() { Target = tUid, Weight = weight });
        }
        comp.TargetList = comp.TargetList.OrderByDescending(t => t.Weight).ToList();
        comp.TargetEntity = comp.TargetList.FirstOrDefault()?.Target; // pick the highest weighted target
    }

    public void PredictiveGuidance(EntityUid uid, HeatSeekingComponent comp, TransformComponent xform, float frameTime) // Predictive Guidance, predicts targets position at impact time.
    {
        if (!comp.TargetEntity.HasValue)
            return;
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return;
        if (!TryComp<PhysicsComponent>(comp.TargetEntity.Value, out var targetPhysics)) // if target has no physics, skip it.
        {
            comp.TargetEntity = null;
            return;
        }
        var entXform = Transform(comp.TargetEntity.Value);
        var distance = Vector2.Distance(
            _transform.ToMapCoordinates(xform.Coordinates).Position,
            _transform.ToMapCoordinates(entXform.Coordinates).Position
        );
        var angle = (
            _transform.ToMapCoordinates(entXform.Coordinates).Position -
            _transform.ToMapCoordinates(xform.Coordinates).Position
        ).ToWorldAngle();

        float dif = (float) Math.Abs(MathHelper.RadiansToDegrees((float) angle) - MathHelper.RadiansToDegrees((float) _transform.GetWorldRotation(xform)) % 360);
        if (dif > 180)
            dif = 360 - dif;
        dif = MathHelper.DegreesToRadians(dif);

        if (dif >= MathHelper.DegreesToRadians(comp.FieldOfView))
        {
            comp.TargetEntity = null;
            return;
        }
        float timeToImpact = distance / (targetPhysics.LinearVelocity - physics.LinearVelocity).Length(); // if target is moving away this will still be positive but ngl its a complete nothingburger
        if (timeToImpact < 0.1f) { timeToImpact = 0.1f; } // if this goes negative it can make the missile point backwards
        Vector2 predictedPosition = _transform.ToMapCoordinates(entXform.Coordinates).Position + targetPhysics.LinearVelocity * timeToImpact;

        Angle targetAngle = (predictedPosition - _transform.ToMapCoordinates(xform.Coordinates).Position).ToWorldAngle();
        targetAngle = ApplyWeave(uid, comp, targetAngle, distance);
        _rotate.TryRotateTo(uid, targetAngle, frameTime, comp.WeaponArc, comp.RotationSpeed?.Theta ?? double.MaxValue, xform);
    }

    public void PurePursuit(EntityUid uid, HeatSeekingComponent comp, TransformComponent xform, float frameTime) // Pure Pursuit, points directly at target.
    {
        if (comp.TargetEntity.HasValue)
        {
            var entXform = Transform(comp.TargetEntity.Value);

            var targetDelta =
                _transform.ToMapCoordinates(entXform.Coordinates).Position -
                _transform.ToMapCoordinates(xform.Coordinates).Position;
            var angle = targetDelta.ToWorldAngle();

            float dif = (float) Math.Abs(MathHelper.RadiansToDegrees((float) angle) - MathHelper.RadiansToDegrees((float) _transform.GetWorldRotation(xform)) % 360);
            if (dif > 180)
                dif = 360 - dif;
            dif = MathHelper.DegreesToRadians(dif);

            if (dif >= MathHelper.DegreesToRadians(comp.FieldOfView))
            {
                comp.TargetEntity = null;
                return;
            }

            angle = ApplyWeave(uid, comp, angle, targetDelta.Length());
            _rotate.TryRotateTo(uid, angle, frameTime, comp.WeaponArc, comp.RotationSpeed?.Theta ?? double.MaxValue, xform);
        }
    }

    private bool IsFriendly(EntityUid? shooterGrid, EntityUid? targetGrid)
    {
        if (shooterGrid is not { } shooter || targetGrid is not { } target)
            return false;

        if (shooter == target)
            return true;

        if (!_iffQuery.TryGetComponent(shooter, out var shooterIff) ||
            !_iffQuery.TryGetComponent(target, out var targetIff))
        {
            return false;
        }

        return _shuttle.GetIFFRelation(shooter, targetIff.Faction, shooterIff) == Relations.Ally;
    }

    private Angle ApplyWeave(EntityUid uid, HeatSeekingComponent comp, Angle targetAngle, float distance)
    {
        if (comp.WeaveAmplitude == Angle.Zero)
            return targetAngle;

        var fade = comp.WeaveFadeDistance > 0f
            ? Math.Clamp(distance / comp.WeaveFadeDistance, 0f, 1f)
            : 1f;

        // Seed off the NetEntity and CurTime rather than anything local, otherwise the client draws the
        // missile weaving out of phase with where the server actually has it.
        var seed = GetNetEntity(uid).Id & int.MaxValue;
        var direction = (seed & 1) == 0 ? 1d : -1d;

        var phase = _timing.CurTime.TotalSeconds * comp.WeaveFrequency * Math.Tau
                    + seed % 64 * (Math.Tau / 64d);
        var offset = comp.WeaveAmplitude.Theta * Math.Sin(phase) * fade * direction;
        return targetAngle + new Angle(offset);
    }
}
