using System.Numerics;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Abilities.Psionics;

/// <summary>
/// The half of the recurrence field that has to run on both sides: the movement penalty for a mob
/// wading through slowed time, and the geometry both sides use to decide what is inside the bubble.
/// Capture bookkeeping, release and the pulse are server business.
/// </summary>
public abstract class SharedRecurrenceFieldSystem : EntitySystem
{
    /// <summary>
    /// Objects are released a little outside the radius they were caught at. Without the margin an
    /// object hovering on the boundary flips between captured and released every tick, stuttering
    /// between crawling and full speed.
    /// </summary>
    public const float ReleaseMargin = 0.35f;

    [Dependency] protected readonly MovementSpeedModifierSystem MoveSpeed = default!;
    [Dependency] protected readonly SharedPhysicsSystem Physics = default!;
    [Dependency] protected readonly SharedTransformSystem XformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TemporallySlowedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<TemporallySlowedComponent, ComponentStartup>(OnSlowStartup);
        SubscribeLocalEvent<TemporallySlowedComponent, ComponentShutdown>(OnSlowShutdown);
        SubscribeLocalEvent<TemporallySlowedComponent, AfterAutoHandleStateEvent>(OnSlowStateHandled);
    }

    private void OnRefreshMoveSpeed(
        EntityUid uid,
        TemporallySlowedComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        // Objects are slowed by scaling their velocity directly; only mobs carry a movement scale.
        if (component.MovementScale is <= 0f or >= 1f)
            return;

        args.ModifySpeed(component.MovementScale, component.MovementScale);
    }

    private void OnSlowStartup(EntityUid uid, TemporallySlowedComponent component, ComponentStartup args)
    {
        MoveSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnSlowShutdown(EntityUid uid, TemporallySlowedComponent component, ComponentShutdown args)
    {
        // The component is still attached to the entity while its own shutdown runs, so the refresh
        // below still reaches OnRefreshMoveSpeed. Clearing the scale first is what makes the refresh
        // undo the slow instead of applying it one last time and leaving the mob crawling forever.
        component.MovementScale = 1f;

        MoveSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnSlowStateHandled(EntityUid uid, TemporallySlowedComponent component, ref AfterAutoHandleStateEvent args)
    {
        MoveSpeed.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    /// How far along the segment from <paramref name="start"/> to <paramref name="end"/> the path
    /// first crosses into the sphere, as a fraction of the segment, or -1 if it never does. Zero
    /// means it started inside.
    /// </summary>
    /// <remarks>
    /// Shared because both sides need it and both have to agree: the server decides where the real
    /// round is caught, and the shooter's own client has to catch its predicted copy of that round
    /// on the same boundary or the two visibly disagree.
    /// </remarks>
    protected static float SegmentEntry(Vector2 start, Vector2 end, Vector2 centre, float radius)
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
    /// The map velocity of whatever a held object's own motion is measured against, or zero if that
    /// entity is in no state to be asked.
    /// </summary>
    /// <remarks>
    /// The guard is load-bearing rather than defensive. The ordinary way an object stops being held
    /// is the field ceasing to exist - it expires, is dispelled, or collapses under a pulse - and by
    /// the time the release runs the field may have lost its transform already. Reading a velocity
    /// off it then resolves a component on a dead entity, which logs an error every single tick a
    /// round is let go.
    /// </remarks>
    protected Vector2 GetFrameVelocity(EntityUid frame)
    {
        return frame.IsValid() && HasComp<TransformComponent>(frame)
            ? Physics.GetMapLinearVelocity(frame)
            : Vector2.Zero;
    }

    /// <summary>
    /// Sets an entity's velocity to <paramref name="velocity"/> as measured against the field. It
    /// goes through the map frame because a body stores its velocity relative to whatever it is
    /// parented to, which is not necessarily what the field is parented to.
    /// </summary>
    protected void SetFieldRelativeVelocity(EntityUid uid, PhysicsComponent body, EntityUid field, Vector2 velocity)
    {
        var target = velocity + GetFrameVelocity(field);
        var difference = target - Physics.GetMapLinearVelocity(uid, body);
        Physics.SetLinearVelocity(uid, body.LinearVelocity + difference, body: body);
    }
}
