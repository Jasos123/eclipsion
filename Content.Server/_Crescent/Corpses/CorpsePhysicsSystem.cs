using Content.Server.Carrying;
using Content.Shared._Crescent.Corpses;
using Content.Shared.Friction;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Crescent.Corpses;

/// <summary>
/// Turns a dead body into dead weight. On death a corpse gets physics damping and extra tile friction
/// so it coasts to a stop, and throwing one barely gets it off the ground.
/// </summary>
/// <remarks>
/// Corpses used to sail across the sector. Tile friction is skipped for weightless bodies and for
/// anything mid-throw, so nothing ever slowed a body down in space or after a shipgun blast, and the
/// throwing impulse divides mass back out - a "heavier" corpse flew exactly as far. Damping is applied
/// by the physics solver and networked with the body, so it works in vacuum, in the air, and after any
/// impulse from any source rather than only after the ones we thought to patch.
///
/// Dragging is deliberately exempt: damping is lifted while someone is pulling the body, otherwise
/// hauling a casualty to medbay fights the corpse the whole way.
/// </remarks>
public sealed class CorpsePhysicsSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TileFrictionController _friction = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast rather than directed on MobStateComponent: only one system may hold the directed
        // MobStateComponent/MobStateChangedEvent pair, and SharedStunSystem already has it.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CorpseWeightComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<CorpseWeightComponent, PullStoppedMessage>(OnPullStopped);

        // After CarryingSystem: that handler is what rewrites ItemUid from the virtual item to the body
        // actually being carried, so before it runs there is no corpse in the event to look at.
        SubscribeLocalEvent<HandsComponent, BeforeThrowEvent>(OnBeforeThrow, after: [typeof(CarryingSystem)]);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            MakeHeavy(args.Target);
        else
            MakeLight(args.Target);
    }

    /// <summary>
    /// Installs corpse damping. Safe to call on a body that already has it - the original values are only
    /// captured the first time, so a mob that dies, is revived and dies again still gets its own physics back.
    /// </summary>
    private void MakeHeavy(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return;

        var comp = EnsureComp<CorpseWeightComponent>(uid);
        if (comp.Applied)
            return;

        comp.OriginalLinearDamping = physics.LinearDamping;
        comp.OriginalAngularDamping = physics.AngularDamping;

        if (TryComp<TileFrictionModifierComponent>(uid, out var friction))
        {
            comp.HadFrictionModifier = true;
            comp.OriginalFrictionModifier = friction.Modifier;
        }

        comp.Applied = true;

        // Someone already hauling the body when it died keeps hauling it unimpeded; the damping goes on
        // as soon as they let go.
        if (!IsBeingPulled(uid))
            ApplyDamping(uid, comp, physics);

        // The modifier has to exist before it can be set: SetModifier resolves the component and logs a
        // failure rather than creating one, and most mobs carry no friction modifier of their own.
        EnsureComp<TileFrictionModifierComponent>(uid);
        _friction.SetModifier(uid, comp.HadFrictionModifier
            ? comp.OriginalFrictionModifier * comp.FrictionModifier
            : comp.FrictionModifier);
    }

    /// <summary>Restores a revived mob's own physics and forgets it was ever a corpse.</summary>
    private void MakeLight(EntityUid uid)
    {
        if (!TryComp<CorpseWeightComponent>(uid, out var comp) || !comp.Applied)
            return;

        if (TryComp<PhysicsComponent>(uid, out var physics))
        {
            _physics.SetLinearDamping(uid, physics, comp.OriginalLinearDamping);
            _physics.SetAngularDamping(uid, physics, comp.OriginalAngularDamping);
        }

        if (comp.HadFrictionModifier)
            _friction.SetModifier(uid, comp.OriginalFrictionModifier);
        else
            RemComp<TileFrictionModifierComponent>(uid);

        // The component itself stays: a prototype may have authored its own numbers on this mob, and
        // deleting it here would silently reset them to the defaults the next time the mob died.
        comp.Applied = false;
        comp.HadFrictionModifier = false;
    }

    private void ApplyDamping(EntityUid uid, CorpseWeightComponent comp, PhysicsComponent physics)
    {
        _physics.SetLinearDamping(uid, physics, comp.LinearDamping);
        _physics.SetAngularDamping(uid, physics, comp.AngularDamping);
    }

    /// <summary>Dragging a body is meant to stay possible, so the damping comes off for as long as it is pulled.</summary>
    private void OnPullStarted(EntityUid uid, CorpseWeightComponent comp, PullStartedMessage args)
    {
        if (args.PulledUid != uid || !comp.Applied || !TryComp<PhysicsComponent>(uid, out var physics))
            return;

        _physics.SetLinearDamping(uid, physics, comp.OriginalLinearDamping);
        _physics.SetAngularDamping(uid, physics, comp.OriginalAngularDamping);
    }

    private void OnPullStopped(EntityUid uid, CorpseWeightComponent comp, PullStoppedMessage args)
    {
        if (args.PulledUid != uid || !comp.Applied || !TryComp<PhysicsComponent>(uid, out var physics))
            return;

        ApplyDamping(uid, comp, physics);
    }

    private bool IsBeingPulled(EntityUid uid)
    {
        return TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled;
    }

    /// <summary>
    /// Cuts the launch speed of a thrown corpse. Mass cannot do this job: the throw impulse is scaled by
    /// mass and then divided by it again, so the only way a body flies less far is to throw it slower.
    /// </summary>
    private void OnBeforeThrow(Entity<HandsComponent> ent, ref BeforeThrowEvent args)
    {
        if (args.Cancelled || !HasComp<MobStateComponent>(args.ItemUid) || !_mobState.IsDead(args.ItemUid))
            return;

        var multiplier = CompOrNull<CorpseWeightComponent>(args.ItemUid)?.ThrowSpeedMultiplier
                         ?? new CorpseWeightComponent().ThrowSpeedMultiplier;

        args.ThrowSpeed *= multiplier;
    }
}
