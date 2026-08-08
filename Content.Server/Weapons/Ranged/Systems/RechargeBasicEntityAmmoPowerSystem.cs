using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.CCvars;
using Content.Shared._Crescent.ShipPower;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Configuration;

namespace Content.Server.Weapons.Ranged.Systems;

/// <summary>
/// Pays for regenerated rounds out of the weapon's own battery. Server only, batteries don't exist clientside.
/// </summary>
public sealed class RechargeBasicEntityAmmoPowerSystem : EntitySystem
{
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;

    private bool _powerDrawEnabled;

    public override void Initialize()
    {
        base.Initialize();

        _powerDrawEnabled = _config.GetCVar(CrescentCVars.ShipSystemsPowerDrawEnabled);
        SubscribeLocalEvent<RechargeBasicEntityAmmoComponent, RechargeBasicEntityAmmoAttemptEvent>(OnRechargeAttempt);
    }

    private void OnRechargeAttempt(
        EntityUid uid,
        RechargeBasicEntityAmmoComponent component,
        ref RechargeBasicEntityAmmoAttemptEvent args)
    {
        if (!_powerDrawEnabled && HasComp<WeaponPowerDrawComponent>(uid))
        {
            args.Allowed = true;
            return;
        }

        args.Allowed = _battery.TryUseCharge(uid, args.EnergyPerCharge);
    }
}
