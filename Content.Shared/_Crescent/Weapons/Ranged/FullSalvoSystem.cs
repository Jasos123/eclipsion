using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._Crescent.Weapons.Ranged;

/// <summary>
/// Keeps saturation launchers from firing partial salvos after an incomplete reload.
/// </summary>
public sealed class FullSalvoSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FullSalvoComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(Entity<FullSalvoComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled || ent.Comp.RequiredShots <= 1)
            return;

        if (!TryComp<GunComponent>(ent, out var gun) || gun.BurstActivated)
            return;

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(ent.Owner, ref ammo);

        var requiredShots = Math.Min(ent.Comp.RequiredShots, ammo.Capacity);
        if (ammo.Count >= requiredShots)
            return;

        args.Cancelled = true;
        args.ResetCooldown = true;
        args.Message = Loc.GetString("gun-full-salvo-not-ready",
            ("count", ammo.Count),
            ("required", requiredShots));
    }
}
