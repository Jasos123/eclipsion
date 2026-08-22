using System.Linq;
using System.Numerics;
using Content.Server._Crescent.HeatSeeking;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Content.Shared.Explosion.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Abilities.Psionics;

/// <summary>
/// Implements the recurrence tree's stasis field and the pulse that collapses it.
///
/// The field is not a physics volume. Fixtures would mean bullets colliding with the bubble instead
/// of entering it, so capture is a swept range query and every held entity carries a
/// <see cref="TemporallySlowedComponent"/> recording the state it arrived in. Release restores that
/// recorded state rather than undoing the arithmetic capture did, so nothing an object gets up to
/// while it is held - a guided missile rewriting its own velocity, the pulse turning a bullet
/// around mid-flight - can turn the field into a way of manufacturing speed.
/// </summary>
public sealed class RecurrenceFieldSystem : SharedRecurrenceFieldSystem
{
    private const string FieldPrototype = "PsionicRecurrenceField";
    private const string PulsePrototype = "EffectPsionicRecurrencePulse";

    /// <summary>
    /// Fastest thing capture can catch, in metres per second, measured against the field itself.
    /// A bullet does not linger inside a 4m bubble - the quickest in the game clear it in a fifth
    /// of a tick - so the range query reaches this far past the field and each candidate is tested
    /// against the path it is about to travel. The ceiling is what that query costs: the fastest
    /// round in the prototypes is 200m/s and the closing speed against a moving ship is well inside
    /// this, but anything past it flies through the field untouched.
    /// </summary>
    private const float MaxSweepSpeed = 400f;

    /// <summary>
    /// How far up the parent chain a countdown is looked for. Enough for a charge in a hand or in a
    /// bag in a backpack, and short enough that walking it every tick costs nothing.
    /// </summary>
    private const int MaxCarryDepth = 4;

    /// <summary>
    /// What the capture query looks for. <see cref="LookupFlags.Sensors"/> is the load-bearing one:
    /// every projectile in the game is a non-hard fixture so that it passes through crates and
    /// people rather than shoving them, and a query without this flag silently returns none of them.
    /// Leaving it out is why the field would slow a person standing in it and let a bullet through.
    /// </summary>
    private const LookupFlags CaptureFlags = LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Sensors;

    /// <summary>
    /// Objects are released a little outside the radius they were caught at. Without the margin an
    /// object hovering on the boundary flips between captured and released every tick, stuttering
    /// between crawling and full speed.
    /// </summary>
    private const float ReleaseMargin = 0.35f;

    /// <summary>
    /// How far a pulsed object is aimed. Throwing takes a displacement, not a direction: a unit
    /// vector gives a flight time of nearly zero, which lands the object instantly instead of
    /// sending it across the room.
    /// </summary>
    private const float PulseThrowDistance = 10f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    /// <summary>
    /// Scratch set for <see cref="Rescan"/>, reused rather than allocated per field per tick.
    /// </summary>
    private readonly HashSet<EntityUid> _released = new();

    public override void Initialize()
    {
        base.Initialize();

        // Capture looks at the step each object is about to take, so it has to run before that step
        // is simulated. Left to the default ordering it could just as easily run after, by which
        // point a round has already crossed the field and hit whatever was standing behind it.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));

        // A heat seeker rewrites its own velocity every tick. Running after it means the hold below
        // is the last word on how fast the thing is going when physics moves it.
        UpdatesAfter.Add(typeof(HeatSeekingSystem));

        // Time is handed back to a fuse before the trigger system takes its slice off, so a charge
        // whose last fraction of a second falls inside the field does not go off anyway.
        UpdatesBefore.Add(typeof(TriggerSystem));

        SubscribeLocalEvent<PsionicStasisFieldActionEvent>(OnStasisField);
        SubscribeLocalEvent<PsionicRecurrencePulseActionEvent>(OnPulse);
        SubscribeLocalEvent<RecurrenceFieldComponent, MapInitEvent>(OnFieldMapInit);
        SubscribeLocalEvent<RecurrenceFieldComponent, ComponentShutdown>(OnFieldShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // Both of these run every tick rather than only at capture: a countdown slows smoothly
        // instead of in visible steps, and anything driving its own velocity is pulled back down
        // before it gets a chance to use the speed it just gave itself.
        HoldTimers(frameTime);
        HoldVelocities();

        var query = EntityQueryEnumerator<RecurrenceFieldComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var field, out var xform))
        {
            if (now >= field.ExpiresAt)
            {
                QueueDel(uid);
                continue;
            }

            Rescan((uid, field), xform, frameTime);
        }
    }

    /// <summary>
    /// Gives back the time a captured fuse would otherwise have burned through. Anything with a
    /// running timer counts here: grenades, breaching charges, anything on a countdown.
    /// </summary>
    private void HoldTimers(float frameTime)
    {
        var query = EntityQueryEnumerator<ActiveTimerTriggerComponent>();
        while (query.MoveNext(out var uid, out var timer))
        {
            if (!TryGetTimeScale(uid, out var scale))
                continue;

            var held = frameTime * (1f - scale);
            timer.TimeRemaining += held;

            // The beeping is the only thing anyone can actually hear a fuse through, so it has to
            // slow with it or a held charge sounds exactly like one counting down normally.
            timer.TimeUntilBeep += held;
        }
    }

    /// <summary>
    /// The rate a countdown runs at, or false if it is not in a field at all. Walks up the parent
    /// chain so a charge in a captured person's hands is held back with them: contained entities are
    /// out of the broadphase, so the range query that drives capture can never see them.
    /// </summary>
    private bool TryGetTimeScale(EntityUid uid, out float scale)
    {
        scale = 1f;
        var current = uid;

        for (var depth = 0; depth < MaxCarryDepth; depth++)
        {
            if (TryComp<TemporallySlowedComponent>(current, out var slowed))
            {
                // Whoever is carrying it may only be waist-deep in the field, but the charge itself
                // is in it, so it ticks at the object rate rather than the carrier's walking rate.
                scale = TryComp<RecurrenceFieldComponent>(slowed.Field, out var field)
                    ? field.TimeScale
                    : slowed.AppliedScale;

                return scale is > 0f and < 1f;
            }

            if (!TryComp<TransformComponent>(current, out var xform) || !xform.ParentUid.IsValid())
                return false;

            current = xform.ParentUid;
        }

        return false;
    }

    /// <summary>
    /// Holds captured objects down to the speed they were caught at. Capture scaling the velocity
    /// once is not enough: a guided missile writes its own velocity every single tick and would
    /// otherwise cross the field at full speed, and anything else that gets shoved while held would
    /// keep the shove.
    /// </summary>
    private void HoldVelocities()
    {
        var query = EntityQueryEnumerator<TemporallySlowedComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var slowed, out var body))
        {
            if (slowed.AppliedScale is <= 0f or >= 1f)
                continue;

            var velocity = _physics.GetMapLinearVelocity(uid, body) - _physics.GetMapLinearVelocity(slowed.Field);
            var speed = velocity.Length();

            // A ceiling, not an assignment. Something that slides to a halt inside the field is left
            // to do it; only speed the object did not have on the way in is taken back off it.
            var ceiling = slowed.EntryVelocity.Length() * slowed.AppliedScale;
            if (speed <= ceiling)
                continue;

            SetFieldRelativeVelocity(uid, body, slowed.Field, speed > 0f ? velocity / speed * ceiling : Vector2.Zero);
        }
    }

    private void OnFieldMapInit(EntityUid uid, RecurrenceFieldComponent component, MapInitEvent args)
    {
        component.ExpiresAt = _timing.CurTime + component.Lifetime;
        Dirty(uid, component);
    }

    private void OnStasisField(PsionicStasisFieldActionEvent args)
    {
        if (args.Handled || !_psionics.OnAttemptPowerUse(args.Performer, "stasis field", true))
            return;

        var target = _transform.ToMapCoordinates(args.Target);
        if (target.MapId != _transform.GetMapCoordinates(args.Performer).MapId)
            return;

        var field = Spawn(FieldPrototype, args.Target);
        var comp = EnsureComp<RecurrenceFieldComponent>(field);

        // Expiry is set by map init; the power only has to say who owns the field.
        comp.Caster = args.Performer;

        // Capture immediately: waiting for the next scan would let anything already in flight sail
        // straight through the field it was cast in front of.
        Rescan((field, comp), Transform(field), 0f);

        _psionics.LogPowerUsed(args.Performer, "stasis field", 6, 9);
        args.Handled = true;
    }

    private void OnPulse(PsionicRecurrencePulseActionEvent args)
    {
        if (args.Handled)
            return;

        // A Psion can have more than one field standing; the pulse collapses all of theirs at once.
        var toCollapse = new List<Entity<RecurrenceFieldComponent>>();
        var query = EntityQueryEnumerator<RecurrenceFieldComponent>();
        while (query.MoveNext(out var uid, out var field))
        {
            if (field.Caster == args.Performer)
                toCollapse.Add((uid, field));
        }

        // Checked before the power is attempted so pressing pulse with nothing up costs nothing.
        if (toCollapse.Count == 0)
        {
            _popup.PopupEntity(
                Loc.GetString("psionic-recurrence-pulse-no-field"),
                args.Performer,
                args.Performer,
                PopupType.SmallCaution);
            return;
        }

        if (!_psionics.OnAttemptPowerUse(args.Performer, "recurrence pulse", true))
            return;

        var casterPos = _transform.GetWorldPosition(args.Performer);
        var pulsed = 0;

        foreach (var field in toCollapse)
            pulsed += CollapseField(field, casterPos, args.Performer);

        _popup.PopupEntity(
            Loc.GetString("psionic-recurrence-pulse-released", ("count", pulsed)),
            args.Performer,
            args.Performer,
            PopupType.Medium);

        _psionics.LogPowerUsed(args.Performer, "recurrence pulse", 5, 8);
        args.Handled = true;
    }

    /// <summary>
    /// Throws everything the field is holding back the way it came and deletes the field.
    /// </summary>
    /// <returns>How many objects were actually launched.</returns>
    private int CollapseField(Entity<RecurrenceFieldComponent> field, Vector2 casterPos, EntityUid caster)
    {
        var fieldPos = _transform.GetWorldPosition(field.Owner);
        var launched = 0;

        // ToArray: releasing mutates the capture set.
        foreach (var held in field.Comp.Captured.ToArray())
        {
            if (Deleted(held) || !TryComp<TemporallySlowedComponent>(held, out var slowed))
                continue;

            // Mobs are not ammunition. They keep the slow until it wears off normally.
            if (HasComp<MobStateComponent>(held))
                continue;

            var direction = GetReturnDirection(slowed, held, fieldPos, casterPos);
            if (direction == Vector2.Zero)
                continue;

            var speed = MathF.Max(
                slowed.EntryVelocity.Length() * field.Comp.PulseSpeedMultiplier,
                field.Comp.PulseMinimumSpeed);

            Release(held, slowed, refreshSpeed: false);
            Launch(held, field.Owner, direction, speed, caster);
            launched++;
        }

        // The effect carries its own sound; see Prototypes/Entities/Effects/psionics.yml.
        Spawn(PulsePrototype, Transform(field.Owner).Coordinates);
        QueueDel(field.Owner);
        return launched;
    }

    /// <summary>
    /// Where a held object should be sent. Anything that flew in goes back down its own path;
    /// anything that was merely sitting there is shoved along the caster's line of sight.
    /// </summary>
    private Vector2 GetReturnDirection(
        TemporallySlowedComponent slowed,
        EntityUid held,
        Vector2 fieldPos,
        Vector2 casterPos)
    {
        if (slowed.EntryVelocity.LengthSquared() > 0.5f)
            return Vector2.Normalize(-slowed.EntryVelocity);

        var outward = fieldPos - casterPos;
        if (outward.LengthSquared() > 0.01f)
            return Vector2.Normalize(outward);

        var drift = _transform.GetWorldPosition(held) - casterPos;
        return drift.LengthSquared() > 0.01f ? Vector2.Normalize(drift) : Vector2.UnitY;
    }

    /// <summary>
    /// Sends one object on its way. Projectiles are re-aimed and re-attributed so a returned bullet
    /// behaves like one the Psion fired; everything else is thrown.
    /// </summary>
    private void Launch(EntityUid uid, EntityUid field, Vector2 direction, float speed, EntityUid caster)
    {
        if (TryComp<ProjectileComponent>(uid, out var projectile))
        {
            if (TryComp<PhysicsComponent>(uid, out var body))
                SetFieldRelativeVelocity(uid, body, field, direction * speed);

            _transform.SetWorldRotation(uid, direction.ToWorldAngle());

            // Without this the bullet still ignores whoever fired it and still credits them for the
            // kill, which is the exact opposite of what the power is for.
            projectile.Shooter = caster;
            projectile.Weapon = caster;
            projectile.DamagedEntity = false;
            Dirty(uid, projectile);
            return;
        }

        // TryThrow adds an impulse on top of whatever the object is already doing, so the momentum
        // it came in with would be subtracted from the momentum it leaves with. Zeroing the local
        // velocity leaves it moving with its parent, which is what a throw aboard a ship should do.
        if (TryComp<PhysicsComponent>(uid, out var physics))
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);

        // The impulse TryThrow applies lands on the body's own velocity, which is stored in the
        // parent's frame, so the aim has to be handed over in that frame too or every pulse thrown
        // aboard a rotated grid leaves at the grid's angle instead of the one asked for.
        var parentRotation = _transform.GetWorldRotation(Transform(uid).ParentUid);
        var localDirection = (-parentRotation).RotateVec(direction);

        _throwing.TryThrow(
            uid,
            localDirection * PulseThrowDistance,
            baseThrowSpeed: speed,
            user: caster,
            // The Psion is collapsing a field, not lobbing the objects by hand: no shove and no
            // camera kick, both of which would fire once per object caught.
            pushbackRatio: 0f,
            recoil: false,
            doSpin: true);
    }

    /// <summary>
    /// Brings one field's capture set in line with what is actually inside it right now.
    /// </summary>
    private void Rescan(Entity<RecurrenceFieldComponent> field, TransformComponent xform, float frameTime)
    {
        var origin = _transform.GetMapCoordinates(field.Owner, xform);
        var radius = field.Comp.Radius;
        var fieldVelocity = _physics.GetMapLinearVelocity(field.Owner);

        _released.Clear();

        foreach (var held in field.Comp.Captured.ToArray())
        {
            if (Deleted(held) || !TryComp<TemporallySlowedComponent>(held, out var slowed))
            {
                field.Comp.Captured.Remove(held);
                continue;
            }

            if (InRange(held, origin, radius + ReleaseMargin))
                continue;

            Release(held, slowed);

            // Held aside for the rest of this scan. It is sitting just outside the boundary with the
            // speed it came in with only just handed back, so the swept test below would read the
            // path it would have taken at that speed, decide it crossed the field, and drag it
            // straight back in - once every tick, forever.
            _released.Add(held);
        }

        // Reaching past the radius by a tick of travel is what makes the field catch bullets at all:
        // anything quicker than about 40m/s crosses a 4m bubble between one tick and the next.
        var sweep = MaxSweepSpeed * frameTime;

        var found = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(origin.MapId, origin.Position, radius + sweep, found, CaptureFlags);

        foreach (var candidate in found)
        {
            if (candidate == field.Owner
                || _released.Contains(candidate)
                || Deleted(candidate)
                || HasComp<TemporallySlowedComponent>(candidate)
                || HasComp<RecurrenceFieldComponent>(candidate)
                || !CanCapture(candidate))
            {
                continue;
            }

            // Looking forward at the step about to be taken, not back at the one already taken. A
            // round caught after the fact has already been through the field and already collided
            // with whatever was on the other side of it.
            var start = _transform.GetWorldPosition(candidate);
            var travel = (_physics.GetMapLinearVelocity(candidate) - fieldVelocity) * frameTime;

            var entry = SegmentEntry(start, start + travel, origin.Position, radius);
            if (entry < 0f)
                continue;

            // Set down on the boundary it is about to cross, so the slow applies for the whole of
            // its passage through rather than from wherever this tick would have carried it to.
            if (entry > 0f && !HasComp<MobStateComponent>(candidate))
                _transform.SetWorldPosition(candidate, start + travel * entry);

            Capture(field, candidate);
        }
    }

    /// <summary>
    /// How far along the segment from <paramref name="start"/> to <paramref name="end"/> the path
    /// first crosses into the sphere, as a fraction of the segment, or -1 if it never does. Zero
    /// means it started inside.
    /// </summary>
    private static float SegmentEntry(Vector2 start, Vector2 end, Vector2 centre, float radius)
    {
        var offset = start - centre;
        var outside = Vector2.Dot(offset, offset) - radius * radius;
        if (outside <= 0f)
            return 0f;

        var travel = end - start;
        var a = Vector2.Dot(travel, travel);
        if (a <= float.Epsilon)
            return -1f;

        var b = 2f * Vector2.Dot(offset, travel);
        var discriminant = b * b - 4f * a * outside;
        if (discriminant < 0f)
            return -1f;

        var hit = (-b - MathF.Sqrt(discriminant)) / (2f * a);
        return hit is >= 0f and <= 1f ? hit : -1f;
    }

    /// <summary>
    /// Sets an entity's velocity to <paramref name="velocity"/> as measured against the field. It
    /// goes through the map frame because a body stores its velocity relative to whatever it is
    /// parented to, which is not necessarily what the field is parented to.
    /// </summary>
    private void SetFieldRelativeVelocity(EntityUid uid, PhysicsComponent body, EntityUid field, Vector2 velocity)
    {
        var target = velocity + _physics.GetMapLinearVelocity(field);
        var difference = target - _physics.GetMapLinearVelocity(uid, body);
        _physics.SetLinearVelocity(uid, body.LinearVelocity + difference, body: body);
    }

    private bool InRange(EntityUid uid, MapCoordinates origin, float radius)
    {
        var xform = Transform(uid);
        if (xform.MapID != origin.MapId)
            return false;

        return (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared() <= radius * radius;
    }

    /// <summary>
    /// Only things that move under their own momentum are worth freezing. Walls, floors and items
    /// sitting in someone's bag are not, and neither is the caster.
    /// </summary>
    private bool CanCapture(EntityUid uid)
    {
        if (Transform(uid).ParentUid is var parent && parent.IsValid() && HasComp<MobStateComponent>(parent))
            return false;

        if (HasComp<MobStateComponent>(uid))
            return true;

        // A charge that was armed and set down is exactly what the field is for, and sitting still
        // is the whole point of it - there is no velocity to notice it by.
        if (HasComp<ActiveTimerTriggerComponent>(uid))
            return true;

        return TryComp<PhysicsComponent>(uid, out var body)
               && body.BodyType == Robust.Shared.Physics.BodyType.Dynamic
               && body.LinearVelocity.LengthSquared() > 0.01f;
    }

    private void Capture(Entity<RecurrenceFieldComponent> field, EntityUid uid)
    {
        var slowed = AddComp<TemporallySlowedComponent>(uid);
        slowed.Field = field.Owner;

        if (HasComp<MobStateComponent>(uid))
        {
            // A mob still walks under its own power, so scaling its velocity would be undone by the
            // very next mover tick. The speed modifier is the only thing that sticks.
            slowed.MovementScale = field.Comp.MobTimeScale;
            slowed.AppliedScale = 1f;

            // AddComp has already run the component's startup, back when the scale was still 1, so
            // without this the server never applies the slow it is about to tell the client about.
            MoveSpeed.RefreshMovementSpeedModifiers(uid);
        }
        else if (TryComp<PhysicsComponent>(uid, out var body))
        {
            slowed.EntryVelocity = _physics.GetMapLinearVelocity(uid, body) - _physics.GetMapLinearVelocity(field.Owner);
            slowed.EntryAngularVelocity = body.AngularVelocity;
            slowed.AppliedScale = field.Comp.TimeScale;

            SetFieldRelativeVelocity(uid, body, field.Owner, slowed.EntryVelocity * field.Comp.TimeScale);
            _physics.SetAngularVelocity(uid, body.AngularVelocity * field.Comp.TimeScale, body: body);
        }

        Dirty(uid, slowed);
        field.Comp.Captured.Add(uid);
    }

    /// <summary>
    /// Undoes a capture. <paramref name="refreshSpeed"/> is false when the caller is about to set a
    /// velocity of its own, so the restore does not fight the launch.
    /// </summary>
    private void Release(EntityUid uid, TemporallySlowedComponent slowed, bool refreshSpeed = true)
    {
        if (TryComp<RecurrenceFieldComponent>(slowed.Field, out var field))
            field.Captured.Remove(uid);

        if (refreshSpeed
            && slowed.AppliedScale is > 0f and < 1f
            && TryComp<PhysicsComponent>(uid, out var body))
        {
            // Give back the speed it was caught with rather than multiplying whatever it is doing
            // now by the inverse of the scale. Anything that rewrites its own velocity while held -
            // a heat seeker does it every single tick - would otherwise leave at eight times its own
            // top speed, with the field as a free accelerator.
            var heading = _physics.GetMapLinearVelocity(uid, body) - _physics.GetMapLinearVelocity(slowed.Field);
            if (heading.LengthSquared() <= 0.0001f)
                heading = slowed.EntryVelocity;

            if (heading.LengthSquared() > 0.0001f)
                heading = Vector2.Normalize(heading);

            SetFieldRelativeVelocity(uid, body, slowed.Field, heading * slowed.EntryVelocity.Length());
            _physics.SetAngularVelocity(uid, slowed.EntryAngularVelocity, body: body);
        }

        RemComp<TemporallySlowedComponent>(uid);
    }

    /// <summary>
    /// A field that expires, is dispelled or is deleted by an admin must not leave objects frozen
    /// in place forever.
    /// </summary>
    private void OnFieldShutdown(EntityUid uid, RecurrenceFieldComponent component, ComponentShutdown args)
    {
        foreach (var held in component.Captured.ToArray())
        {
            if (!Deleted(held) && TryComp<TemporallySlowedComponent>(held, out var slowed))
                Release(held, slowed);
        }

        component.Captured.Clear();
    }
}
