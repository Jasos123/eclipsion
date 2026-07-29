using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Crescent.Barricades;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;

namespace Content.Server._Crescent.Barricades;

/// <summary>
/// Replaces a barricade with an upgraded prototype while preserving its runtime condition.
/// </summary>
public sealed partial class BarricadeUpgradeSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BarricadeUpgradeableComponent, InteractUsingEvent>(OnUpgradeInteractUsing);
    }

    private void OnUpgradeInteractUsing(Entity<BarricadeUpgradeableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !TryComp(args.Used, out BarricadeUpgradeKitComponent? kit) ||
            !ent.Comp.Upgrades.TryGetValue(kit.Upgrade, out var upgradedPrototype))
            return;

        args.Handled = true;

        var oldTransform = Transform(ent);
        var upgraded = SpawnAtPosition(upgradedPrototype, oldTransform.Coordinates);
        _transform.SetLocalRotation(upgraded, oldTransform.LocalRotation);

        TransferCondition(ent.Owner, upgraded);

        if (TryComp(args.Used, out StackComponent? stack))
            _stack.Use(args.Used, 1, stack);
        else
            QueueDel(args.Used);

        QueueDel(ent);
        _popup.PopupEntity(Loc.GetString("crescent-barricade-upgrade-applied"), upgraded, args.User);
    }

    private void TransferCondition(EntityUid oldBarricade, EntityUid upgraded)
    {
        if (TryComp(oldBarricade, out DamageableComponent? oldDamageable) &&
            TryComp(upgraded, out DamageableComponent? newDamageable))
        {
            _damageable.SetDamage(upgraded, newDamageable, new DamageSpecifier(oldDamageable.Damage));
        }

        if (TryComp(oldBarricade, out BarricadeBarbedComponent? oldBarbed) &&
            TryComp(upgraded, out BarricadeBarbedComponent? newBarbed))
        {
            newBarbed.IsBarbed = oldBarbed.IsBarbed;
            Dirty(upgraded, newBarbed);
            _appearance.SetData(upgraded, BarricadeVisuals.Barbed, newBarbed.IsBarbed);
        }

        if (!TryComp(oldBarricade, out FlammableComponent? oldFlammable) ||
            !TryComp(upgraded, out FlammableComponent? newFlammable))
            return;

        _flammable.SetFireStacks(upgraded, oldFlammable.FireStacks, newFlammable);
        if (oldFlammable.OnFire && newFlammable.FireStacks > 0f)
            _flammable.Ignite(upgraded, oldBarricade, newFlammable);
    }
}
