using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Events;

namespace Content.Shared.Damage.Components;

public sealed partial class RequireProjectileTargetSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RequireProjectileTargetComponent, PreventCollideEvent>(PreventCollide);
        SubscribeLocalEvent<RequireProjectileTargetComponent, StoodEvent>(StandingBulletHit);
        SubscribeLocalEvent<RequireProjectileTargetComponent, DownedEvent>(LayingBulletPass);
    }

    private void PreventCollide(Entity<RequireProjectileTargetComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!ent.Comp.Active)
            return;

        var other = args.OtherEntity;
        if (!TryComp(other, out ProjectileComponent? projectile) ||
            CompOrNull<TargetedProjectileComponent>(other)?.Target == ent)
        {
            return;
        }

        var shooter = projectile.Shooter;
        if (!shooter.HasValue || TerminatingOrDeleted(shooter.Value))
            return;

        // A projectile fired from inside a crate must still collide with the crate.
        if (_container.IsEntityOrParentInContainer(shooter.Value))
            return;

        // Lying-target misses are resolved by the projectile system. This component is also used by
        // structures, which should only intercept a shot when the player deliberately targets them.
        if (!HasComp<StandingStateComponent>(ent))
            args.Cancelled = true;
    }

    private void SetActive(Entity<RequireProjectileTargetComponent> ent, bool value)
    {
        if (ent.Comp.Active == value)
            return;

        ent.Comp.Active = value;
        Dirty(ent);
    }

    private void StandingBulletHit(Entity<RequireProjectileTargetComponent> ent, ref StoodEvent args)
    {
        SetActive(ent, false);
    }

    private void LayingBulletPass(Entity<RequireProjectileTargetComponent> ent, ref DownedEvent args)
    {
        SetActive(ent, true);
    }
}
