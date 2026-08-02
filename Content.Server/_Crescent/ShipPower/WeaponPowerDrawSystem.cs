using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.ShipPower;
using Content.Shared.Examine;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._Crescent.ShipPower;

/// <summary>
/// Bills a ship weapon's battery for every shot, and trims a burst down to what it can actually afford.
/// </summary>
public sealed class WeaponPowerDrawSystem : EntitySystem
{
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeaponPowerDrawComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<WeaponPowerDrawComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<WeaponPowerDrawComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAttemptShoot(Entity<WeaponPowerDrawComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled || args.Shots <= 0 || ent.Comp.EnergyPerShot <= 0f)
            return;

        if (!TryComp<BatteryComponent>(ent, out var battery))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("ship-weapon-no-charge");
            return;
        }

        var affordableShots = (int) Math.Min(
            args.Shots,
            MathF.Floor(battery.CurrentCharge / ent.Comp.EnergyPerShot));

        if (affordableShots > 0)
        {
            args.Shots = affordableShots;
            return;
        }

        args.Cancelled = true;
        args.Message = Loc.GetString("ship-weapon-no-charge");
    }

    private void OnGunShot(Entity<WeaponPowerDrawComponent> ent, ref GunShotEvent args)
    {
        if (ent.Comp.EnergyPerShot <= 0f)
            return;

        var shots = Math.Max(1, args.Ammo.Count);
        _battery.UseCharge(ent.Owner, ent.Comp.EnergyPerShot * shots);
    }

    private void OnExamined(Entity<WeaponPowerDrawComponent> ent, ref ExaminedEvent args)
    {
        if (!TryComp<BatteryComponent>(ent, out var battery))
            return;

        args.PushMarkup(Loc.GetString("ship-weapon-power-examine",
            ("shot", (int) ent.Comp.EnergyPerShot),
            ("charge", (int) battery.CurrentCharge),
            ("max", (int) battery.MaxCharge)));

        if (TryComp<ApcPowerReceiverBatteryComponent>(ent, out var apcBattery))
        {
            args.PushMarkup(Loc.GetString("ship-weapon-power-examine-draw",
                ("draw", (int) apcBattery.BatteryRechargeRate)));
        }

        if (battery.CurrentCharge < ent.Comp.EnergyPerShot)
            args.PushMarkup(Loc.GetString("ship-weapon-power-examine-flat"));
    }
}
