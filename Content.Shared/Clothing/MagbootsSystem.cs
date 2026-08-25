using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Atmos.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Slippery;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared.Clothing;

public sealed class SharedMagbootsSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedJetpackSystem _jetpack = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MagbootsComponent, ItemToggledEvent>(OnToggled);
        // Crescent: magboots and jetpacks are mutually exclusive, see OnJetpackUserStartup.
        SubscribeLocalEvent<JetpackUserComponent, ComponentStartup>(OnJetpackUserStartup);
        SubscribeLocalEvent<MagbootsComponent, ClothingGotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<MagbootsComponent, ClothingGotUnequippedEvent>(OnGotUnequipped);
        SubscribeLocalEvent<MagbootsComponent, IsWeightlessEvent>(OnIsWeightless);
        SubscribeLocalEvent<MagbootsComponent, InventoryRelayedEvent<IsWeightlessEvent>>(OnIsWeightless);
        SubscribeLocalEvent<MagbootsComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<MagbootsComponent, SlipAttemptEvent>(OnSlipAttempt);
    }

    private void OnToggled(Entity<MagbootsComponent> ent, ref ItemToggledEvent args)
    {
        var (uid, comp) = ent;
        comp.Active = args.Activated;
        // only stick to the floor if being worn in the correct slot
        if (_container.TryGetContainingContainer((uid, null, null), out var container) &&
            _inventory.TryGetSlotEntity(container.Owner, comp.Slot, out var worn)
            && uid == worn)
        {
            UpdateMagbootEffects(container.Owner, ent, args.Activated);

            // Crescent: turning the magnets on is how you ask to stand still, so cut the thrusters.
            // Otherwise the jetpack keeps you weightless (see OnIsWeightless) and the boots do nothing at all,
            // which reads as "my magboots are broken".
            if (args.Activated)
                DisableJetpack(container.Owner);
        }

        if (comp.ChangeClothingVisuals)
        {
            var prefix = args.Activated ? "on" : null;
            _item.SetHeldPrefix(ent, prefix);
            _clothing.SetEquippedPrefix(ent, prefix);
        }
    }

    /// <summary>
    /// Crescent: the other half of the magboot/jetpack interlock - lighting the thrusters releases the magnets.
    /// Whichever the player toggled last wins.
    /// </summary>
    private void OnJetpackUserStartup(Entity<JetpackUserComponent> ent, ref ComponentStartup args)
    {
        // The component is networked, so this also fires on the client while it applies server state.
        // Toggling items from inside state application would fight the very state being applied.
        if (_timing.ApplyingState)
            return;

        var enumerator = _inventory.GetSlotEnumerator(ent.Owner);
        while (enumerator.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } worn)
                continue;

            if (!TryComp<MagbootsComponent>(worn, out var boots) || !boots.Active)
                continue;

            _toggle.TryDeactivate(worn, ent.Owner);
        }
    }

    private void DisableJetpack(EntityUid user)
    {
        if (!TryComp<JetpackUserComponent>(user, out var jetpackUser))
            return;

        if (TryComp<JetpackComponent>(jetpackUser.Jetpack, out var jetpack))
            _jetpack.SetEnabled(jetpackUser.Jetpack, jetpack, false, user);
    }

    private void OnRefreshMoveSpeed(EntityUid uid, MagbootsComponent component, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var walkModifier = component.Active ? component.ActiveWalkModifier : component.InactiveWalkModifier;
        var sprintModifier = component.Active ? component.ActiveSprintModifier : component.InactiveSprintModifier;
        args.Args.ModifySpeed(walkModifier, sprintModifier);
    }

    private void OnSlipAttempt(EntityUid uid, MagbootsComponent component, SlipAttemptEvent args)
    {
        if (!component.Active)
            return;

        args.Cancel();
    }

    private void OnGotUnequipped(Entity<MagbootsComponent> ent, ref ClothingGotUnequippedEvent args) =>
        UpdateMagbootEffects(args.Wearer, ent, false);

    private void OnGotEquipped(Entity<MagbootsComponent> ent, ref ClothingGotEquippedEvent args) =>
        UpdateMagbootEffects(args.Wearer, ent, _toggle.IsActivated(ent.Owner));

    private void OnIsWeightless(Entity<MagbootsComponent> ent, ref InventoryRelayedEvent<IsWeightlessEvent> args) =>
        OnIsWeightless(ent, ref args.Args);

    public void UpdateMagbootEffects(EntityUid user, Entity<MagbootsComponent> ent, bool state)
    {
        // TODO: public api for this and add access
        if (TryComp<MovedByPressureComponent>(user, out var moved))
            moved.Enabled = !state;

        if (state)
            _alerts.ShowAlert(user, ent.Comp.MagbootsAlert);
        else
            _alerts.ClearAlert(user, ent.Comp.MagbootsAlert);
    }

    private void OnIsWeightless(Entity<MagbootsComponent> ent, ref IsWeightlessEvent args)
    {
        if (args.Handled || !ent.Comp.Active)
            return;

        // Crescent: an active jetpack keeps its user weightless, magboots or not.
        // Otherwise the pair grounds you on a zero-gravity grid while the jetpack is still
        // the mover, which hands out full ground traction at the jetpack's own unmodified
        // speed - no drift, no magboot/hardsuit slowdown.
        if (HasComp<JetpackUserComponent>(args.Entity))
            return;

        // do not cancel weightlessness if the person is in off-grid.
        if (ent.Comp.RequiresGrid && !_gravity.EntityOnGravitySupportingGridOrMap(ent.Owner))
            return;

        args.IsWeightless = false;
        args.Handled = true;
    }
}

public sealed partial class ToggleMagbootsEvent : InstantActionEvent {}
