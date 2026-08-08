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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MagbootsComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<MagbootsComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<MagbootsComponent, ClothingGotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<MagbootsComponent, ClothingGotUnequippedEvent>(OnGotUnequipped);
        SubscribeLocalEvent<MagbootsComponent, IsWeightlessEvent>(OnIsWeightless);
        SubscribeLocalEvent<MagbootsComponent, InventoryRelayedEvent<IsWeightlessEvent>>(OnIsWeightless);
        SubscribeLocalEvent<MagbootsComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<MagbootsComponent, SlipAttemptEvent>(OnSlipAttempt);
    }

    private void OnActivateAttempt(Entity<MagbootsComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (args.Cancelled ||
            !TryGetWearer(ent, out var wearer) ||
            !HasComp<JetpackUserComponent>(wearer))
        {
            return;
        }

        args.Cancelled = true;
        args.Popup = Loc.GetString("magboots-jetpack-active");
    }

    private void OnToggled(Entity<MagbootsComponent> ent, ref ItemToggledEvent args)
    {
        var (uid, comp) = ent;
        comp.Active = args.Activated;
        // only stick to the floor if being worn in the correct slot
        if (TryGetWearer(ent, out var wearer))
            UpdateMagbootEffects(wearer, ent, args.Activated);

        if (comp.ChangeClothingVisuals)
        {
            var prefix = args.Activated ? "on" : null;
            _item.SetHeldPrefix(ent, prefix);
            _clothing.SetEquippedPrefix(ent, prefix);
        }
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

    private void OnGotEquipped(Entity<MagbootsComponent> ent, ref ClothingGotEquippedEvent args)
    {
        var active = _toggle.IsActivated(ent.Owner);

        // Equipping an already active pair bypasses OnActivateAttempt.
        if (active && HasComp<JetpackUserComponent>(args.Wearer))
        {
            _toggle.TryDeactivate(ent.Owner, args.Wearer);
            return;
        }

        UpdateMagbootEffects(args.Wearer, ent, active);
    }

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
        // Keep jetpack movement weightless if both states somehow become active.
        if (args.Handled || !ent.Comp.Active || HasComp<JetpackUserComponent>(args.Entity))
            return;

        // do not cancel weightlessness if the person is in off-grid.
        if (ent.Comp.RequiresGrid && !_gravity.EntityOnGravitySupportingGridOrMap(ent.Owner))
            return;

        args.IsWeightless = false;
        args.Handled = true;
    }

    public bool HasActiveMagboots(EntityUid user)
    {
        var enumerator = _inventory.GetSlotEnumerator(user);
        while (enumerator.NextItem(out var item, out var slot))
        {
            if (TryComp<MagbootsComponent>(item, out var magboots) &&
                magboots.Active &&
                magboots.Slot == slot.Name)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetWearer(Entity<MagbootsComponent> ent, out EntityUid wearer)
    {
        wearer = default;

        if (!_container.TryGetContainingContainer((ent.Owner, null, null), out var container) ||
            !_inventory.TryGetSlotEntity(container.Owner, ent.Comp.Slot, out var worn) ||
            ent.Owner != worn)
        {
            return false;
        }

        wearer = container.Owner;
        return true;
    }
}

public sealed partial class ToggleMagbootsEvent : InstantActionEvent {}
