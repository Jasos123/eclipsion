using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Crescent.Weapons.Ranged;

public sealed class BarricadeLmgSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BarricadeLmgComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BarricadeLmgComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<BarricadeLmgComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<BarricadeLmgComponent, GunRefreshModifiersEvent>(OnRefreshModifiers);
    }

    private void OnMapInit(Entity<BarricadeLmgComponent> ent, ref MapInitEvent args)
    {
        UpdateState(ent, TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded);
    }

    private void OnWielded(Entity<BarricadeLmgComponent> ent, ref ItemWieldedEvent args)
    {
        UpdateState(ent, true);
        _gun.RefreshModifiers(ent.Owner);
    }

    private void OnUnwielded(Entity<BarricadeLmgComponent> ent, ref ItemUnwieldedEvent args)
    {
        UpdateState(ent, false);
        _gun.RefreshModifiers(ent.Owner);
    }

    private void OnRefreshModifiers(Entity<BarricadeLmgComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!TryComp<WieldableComponent>(ent, out var wieldable) || !wieldable.Wielded)
            return;

        args.MinAngle += ent.Comp.MinAngleBonus;
        args.MaxAngle += ent.Comp.MaxAngleBonus;
        args.AngleIncrease += ent.Comp.AngleIncreaseBonus;
        args.AngleDecay += ent.Comp.AngleDecayBonus;
        args.CameraRecoilScalar *= ent.Comp.CameraRecoilMultiplier;
    }

    private void UpdateState(Entity<BarricadeLmgComponent> ent, bool deployed)
    {
        _appearance.SetData(ent.Owner, BarricadeLmgVisuals.Deployed, deployed);
    }
}
