using System.Numerics;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Projectiles;
using Robust.Client.Graphics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Client.Psionics;

/// <summary>
/// Client half of the recurrence field. Movement prediction for mobs comes from the shared base;
/// this owns the greyscale overlay's lifetime and the slow applied to predicted gunfire.
/// </summary>
/// <remarks>
/// The second job is what makes the power look like it works to the person using it. With
/// <c>rmc.gun_prediction</c> on, a shooter's client spawns its own throwaway copy of every round it
/// fires and hides the server's real one from that player, so the only bullet the shooter can see is
/// a client-side entity the server has never heard of. Capture runs on the server, so without the
/// mirror below the shooter watches their own fire cross a stasis field at full speed while everyone
/// else watches the same round crawl.
/// </remarks>
public sealed class RecurrenceFieldSystem : SharedRecurrenceFieldSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>
    /// Scratch list so releasing can mutate components without the query still walking them.
    /// </summary>
    private readonly List<EntityUid> _releasing = new();

    /// <summary>
    /// The same, for the rounds a pulse is about to throw back.
    /// </summary>
    private readonly List<EntityUid> _pulsing = new();

    public override void Initialize()
    {
        base.Initialize();

        // The hold has to land before the step it is holding back is simulated, exactly as it does
        // on the server.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));

        SubscribeNetworkEvent<RecurrencePulseEvent>(OnPulse);

        // The overlay costs one entity query per frame and bails immediately when no field is up,
        // so it is cheaper to leave registered than to add and remove it per field.
        _overlay.AddOverlay(new RecurrenceFieldOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<RecurrenceFieldOverlay>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Prediction replays the same tick repeatedly, and the gun prediction system puts a
        // predicted round back where it started on every replay. Only the pass that actually moves
        // it needs holding back; scaling on the replays would compound into a dead stop.
        if (!_timing.IsFirstTimePredicted)
            return;

        HoldPredicted(frameTime);
        CapturePredicted(frameTime);
    }

    /// <summary>
    /// Keeps rounds already held down to the speed they were caught at, and lets go of the ones that
    /// have crawled out the far side.
    /// </summary>
    private void HoldPredicted(float frameTime)
    {
        _releasing.Clear();

        var query = EntityQueryEnumerator<PredictedProjectileStasisComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var slowed, out var body, out var xform))
        {
            if (!TryComp<RecurrenceFieldComponent>(slowed.Field, out var field))
            {
                _releasing.Add(uid);
                continue;
            }

            var origin = XformSystem.GetMapCoordinates(slowed.Field);
            var offset = XformSystem.GetWorldPosition(xform) - origin.Position;
            var bound = field.Radius + ReleaseMargin;

            if (xform.MapID != origin.MapId || offset.LengthSquared() > bound * bound)
            {
                _releasing.Add(uid);
                continue;
            }

            // The despawn timer is the only clock a bullet runs on, and it is not slowed by anything
            // else. Without this the shooter's round quietly expires inside the bubble while the
            // server's copy - whose timer does get held - sails out the other side.
            if (TryComp<TimedDespawnComponent>(uid, out var despawn))
                despawn.Lifetime += frameTime * (1f - slowed.AppliedScale);

            // A ceiling, not an assignment, so a round that is losing speed inside the field is left
            // to lose it.
            var velocity = Physics.GetMapLinearVelocity(uid, body) - GetFrameVelocity(slowed.Field);
            var speed = velocity.Length();
            var ceiling = slowed.EntryVelocity.Length() * slowed.AppliedScale;

            if (speed > ceiling)
                SetFieldRelativeVelocity(uid, body, slowed.Field, speed > 0f ? velocity / speed * ceiling : Vector2.Zero);
        }

        foreach (var uid in _releasing)
            ReleasePredicted(uid);
    }

    /// <summary>
    /// Catches predicted rounds on the boundary they are about to cross, using the same swept test
    /// the server runs, so the bullet the shooter sees stops where everyone else's stops.
    /// </summary>
    private void CapturePredicted(float frameTime)
    {
        var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, PhysicsComponent, TransformComponent>();
        while (projectiles.MoveNext(out var uid, out _, out var body, out var xform))
        {
            if (HasComp<PredictedProjectileStasisComponent>(uid))
                continue;

            var start = XformSystem.GetWorldPosition(xform);
            var mapId = xform.MapID;

            var fields = EntityQueryEnumerator<RecurrenceFieldComponent, TransformComponent>();
            while (fields.MoveNext(out var fieldUid, out var field, out var fieldXform))
            {
                var origin = XformSystem.GetMapCoordinates(fieldUid, fieldXform);
                if (origin.MapId != mapId)
                    continue;

                var fieldVelocity = GetFrameVelocity(fieldUid);
                var travel = (Physics.GetMapLinearVelocity(uid, body) - fieldVelocity) * frameTime;

                var entry = SegmentEntry(start, start + travel, origin.Position, field.Radius);
                if (entry < 0f)
                    continue;

                // Set down on the rim it is about to cross, so the slow covers the whole passage
                // rather than starting from wherever this tick would have carried it to.
                if (entry > 0f)
                    XformSystem.SetWorldPosition(uid, start + travel * entry);

                Capture(uid, body, (fieldUid, field));
                break;
            }
        }
    }

    private void Capture(EntityUid uid, PhysicsComponent body, Entity<RecurrenceFieldComponent> field)
    {
        var slowed = AddComp<PredictedProjectileStasisComponent>(uid);
        slowed.Field = field.Owner;
        slowed.FieldNet = GetNetEntity(field.Owner);
        slowed.EntryVelocity = Physics.GetMapLinearVelocity(uid, body) - GetFrameVelocity(field.Owner);
        slowed.AppliedScale = field.Comp.TimeScale;

        SetFieldRelativeVelocity(uid, body, field.Owner, slowed.EntryVelocity * field.Comp.TimeScale);
        Physics.SetAngularVelocity(uid, body.AngularVelocity * field.Comp.TimeScale, body: body);
    }

    /// <summary>
    /// The entity a held round's velocity is measured against. Normally the field, but a round is
    /// let go precisely because the field stopped existing - it expired, was dispelled, or collapsed
    /// - so by then reading a velocity off it resolves a transform on a dead entity. Whatever the
    /// round is riding on is the same grid the field was on, so it gives the same answer.
    /// </summary>
    private EntityUid ResolveFrame(EntityUid uid, EntityUid field)
    {
        return field.IsValid() && HasComp<TransformComponent>(field) ? field : Transform(uid).ParentUid;
    }

    /// <summary>
    /// Turns the rounds this client is holding for a collapsing field back down the paths they came
    /// in on, matching what the server has just done to the real ones.
    /// </summary>
    private void OnPulse(RecurrencePulseEvent ev)
    {
        _pulsing.Clear();

        var query = EntityQueryEnumerator<PredictedProjectileStasisComponent>();
        while (query.MoveNext(out var uid, out var slowed))
        {
            if (slowed.FieldNet == ev.Field)
                _pulsing.Add(uid);
        }

        foreach (var uid in _pulsing)
            LaunchPredicted(uid, ev);
    }

    /// <summary>
    /// Sends one held round back the way it came. Anything that was not actually flying when it was
    /// caught is simply let go: the server aims those along the caster's line of sight, and a
    /// predicted round is never one of them.
    /// </summary>
    private void LaunchPredicted(EntityUid uid, RecurrencePulseEvent ev)
    {
        if (!TryComp<PredictedProjectileStasisComponent>(uid, out var slowed)
            || !TryComp<PhysicsComponent>(uid, out var body))
        {
            return;
        }

        if (slowed.EntryVelocity.LengthSquared() <= 0.5f)
        {
            ReleasePredicted(uid);
            return;
        }

        var direction = Vector2.Normalize(-slowed.EntryVelocity);
        var speed = MathF.Max(slowed.EntryVelocity.Length() * ev.SpeedMultiplier, ev.MinimumSpeed);

        // The field is deleted in the same breath as the pulse is announced, so this is very often
        // already gone by now.
        var frame = ResolveFrame(uid, slowed.Field);

        // Before the launch, or the ceiling in HoldPredicted would take the speed straight back off
        // it on the next tick.
        RemComp<PredictedProjectileStasisComponent>(uid);

        SetFieldRelativeVelocity(uid, body, frame, direction * speed);
        XformSystem.SetWorldRotation(uid, direction.ToWorldAngle());

        // The round belongs to the Psion now. Without this the shooter's own copy keeps ignoring
        // them and sails through, while the server's copy - which has been re-attributed - hits.
        if (TryComp<ProjectileComponent>(uid, out var projectile))
        {
            var caster = GetEntity(ev.Caster);
            projectile.Shooter = caster;
            projectile.Weapon = caster;
            projectile.DamagedEntity = false;
        }
    }

    /// <summary>
    /// Hands back the speed the round was caught with, aimed wherever it is pointing now. Restoring
    /// the recorded speed rather than dividing the scale back out is what stops a round that was
    /// shoved while held from leaving faster than it arrived.
    /// </summary>
    private void ReleasePredicted(EntityUid uid)
    {
        if (!TryComp<PredictedProjectileStasisComponent>(uid, out var slowed))
            return;

        if (TryComp<PhysicsComponent>(uid, out var body) && slowed.AppliedScale is > 0f and < 1f)
        {
            // The most common way to get here is the field ceasing to exist, so this must not read
            // anything off it.
            var frame = ResolveFrame(uid, slowed.Field);

            var heading = Physics.GetMapLinearVelocity(uid, body) - GetFrameVelocity(frame);
            if (heading.LengthSquared() <= 0.0001f)
                heading = slowed.EntryVelocity;

            if (heading.LengthSquared() > 0.0001f)
                heading = Vector2.Normalize(heading);

            SetFieldRelativeVelocity(uid, body, frame, heading * slowed.EntryVelocity.Length());
        }

        RemComp<PredictedProjectileStasisComponent>(uid);
    }
}
