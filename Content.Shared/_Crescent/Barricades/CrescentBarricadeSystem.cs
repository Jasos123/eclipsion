using System.Numerics;
using Content.Shared.Climbing.Events;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Crescent.Barricades;

public sealed class CrescentBarricadeSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DirectionalBarricadeComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<BarricadeBarbedComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<BarricadeBarbedComponent, AttemptClimbEvent>(OnAttemptClimb);
        SubscribeLocalEvent<BarricadeBarbedComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BarricadeBarbedComponent, ComponentStartup>(OnStartup);
    }

    private void OnPreventCollide(Entity<DirectionalBarricadeComponent> ent, ref PreventCollideEvent args)
    {
        if (!TryComp(args.OtherEntity, out ProjectileComponent? projectile) ||
            !TryComp(args.OtherEntity, out PhysicsComponent? projectileBody))
            return;

        var velocity = _physics.GetMapLinearVelocity(args.OtherEntity, projectileBody);
        if (velocity.LengthSquared() <= 0.001f)
            return;

        var facing = _transform.GetWorldRotation(ent.Owner).ToWorldVec();
        if (Vector2.Dot(velocity.Normalized(), facing) <= ent.Comp.PassDotThreshold)
            return;

        // Direction alone is insufficient: an outside projectile can otherwise be admitted after
        // a ricochet, a spawn overlap, or an oblique collision. Only shots whose shooter is
        // actually on the protected side are allowed through.
        if (projectile.Shooter is not { } shooter ||
            Deleted(shooter) ||
            Transform(shooter).MapID != Transform(ent).MapID)
            return;

        var shooterOffset = _transform.GetWorldPosition(shooter) - _transform.GetWorldPosition(ent.Owner);
        if (Vector2.Dot(shooterOffset, facing) < -ent.Comp.ProtectedSideMargin)
            args.Cancelled = true;
    }

    private void OnInteractUsing(Entity<BarricadeBarbedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!ent.Comp.IsBarbed && HasComp<BarricadeWireComponent>(args.Used))
        {
            if (TryComp(args.Used, out StackComponent? stack))
                _stack.Use(args.Used, 1, stack);
            else
                QueueDel(args.Used);

            ent.Comp.IsBarbed = true;
            Dirty(ent);
            UpdateAppearance(ent);
            _popup.PopupClient(Loc.GetString("crescent-barricade-wire-attached"), ent, args.User);
            args.Handled = true;
            return;
        }

        if (!ent.Comp.IsBarbed ||
            !TryComp(args.Used, out ToolComponent? tool) ||
            !_tool.HasQuality(args.Used, "Cutting", tool))
            return;

        ent.Comp.IsBarbed = false;
        Dirty(ent);
        UpdateAppearance(ent);

        if (_net.IsServer)
            Spawn(ent.Comp.WirePrototype, Transform(ent).Coordinates);

        _popup.PopupClient(Loc.GetString("crescent-barricade-wire-removed"), ent, args.User);
        args.Handled = true;
    }

    private void OnAttemptClimb(Entity<BarricadeBarbedComponent> ent, ref AttemptClimbEvent args)
    {
        if (!ent.Comp.IsBarbed)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("crescent-barricade-wire-cant-climb"), ent, args.User);
    }

    private void OnExamined(Entity<BarricadeBarbedComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.IsBarbed)
            args.PushMarkup(Loc.GetString("crescent-barricade-wire-examine"));
    }

    private void OnStartup(Entity<BarricadeBarbedComponent> ent, ref ComponentStartup args)
    {
        UpdateAppearance(ent);
    }

    private void UpdateAppearance(Entity<BarricadeBarbedComponent> ent)
    {
        _appearance.SetData(ent, BarricadeVisuals.Barbed, ent.Comp.IsBarbed);
    }

}
