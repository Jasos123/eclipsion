using Content.Shared.Movement.Systems;

namespace Content.Shared.Abilities.Psionics;

/// <summary>
/// The half of the recurrence field that has to run on both sides: the movement penalty for a mob
/// wading through slowed time. Capture, release and the pulse are server business.
/// </summary>
public abstract class SharedRecurrenceFieldSystem : EntitySystem
{
    [Dependency] protected readonly MovementSpeedModifierSystem MoveSpeed = default!;

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
}
