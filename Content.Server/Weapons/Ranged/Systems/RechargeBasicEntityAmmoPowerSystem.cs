using Content.Server.Power.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server.Weapons.Ranged.Systems;

/// <summary>
/// Pays for regenerated rounds out of the weapon's own battery. Server only, batteries don't exist clientside.
/// </summary>
public sealed class RechargeBasicEntityAmmoPowerSystem : EntitySystem
{
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RechargeBasicEntityAmmoComponent, RechargeBasicEntityAmmoAttemptEvent>(OnRechargeAttempt);
    }

    private void OnRechargeAttempt(
        EntityUid uid,
        RechargeBasicEntityAmmoComponent component,
        ref RechargeBasicEntityAmmoAttemptEvent args)
    {
        args.Allowed = _battery.TryUseCharge(uid, args.EnergyPerCharge);
    }
}
