using System.Numerics;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Abilities.Psionics;

/// <summary>
/// The Fire tree's opening node. Hurls a bolt of psionic flame at a chosen spot.
/// </summary>
/// <remarks>
/// This used to be an anomaly power that scattered flare effects around the caster, which meant the
/// node called Fireball was in practice a flashbang: it blinded people and never set anything on
/// fire. It throws an actual projectile now, deliberately weaker than the magic <c>ProjectileFireball</c>
/// a wizard gets - the blast cannot break tiles and will not open a room to space.
/// </remarks>
public sealed class PsionicFireballPowerSystem : EntitySystem
{
    private const string ProjectilePrototype = "ProjectilePsionicFireball";
    private const string PowerName = "pyrokinetic flare";

    /// <summary>
    /// Metres per second. Slower than a bullet on purpose: a fireball you can see coming and step
    /// out of the way of is the trade for it landing an explosion rather than a single hit.
    /// </summary>
    private const float ProjectileSpeed = 14f;

    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PsionicFireballActionEvent>(OnFireball);
    }

    private void OnFireball(PsionicFireballActionEvent args)
    {
        if (args.Handled || !_psionics.OnAttemptPowerUse(args.Performer, PowerName, true))
            return;

        var origin = Transform(args.Performer).Coordinates;
        var originMap = _transform.ToMapCoordinates(origin);
        var targetMap = _transform.ToMapCoordinates(args.Target);
        if (originMap.MapId != targetMap.MapId)
            return;

        var direction = targetMap.Position - originMap.Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        // Spawn on the caster's grid where there is one, so the bolt inherits the ship's motion
        // instead of being left behind by it the moment it exists.
        var spawnCoords = _map.TryFindGridAt(originMap, out var gridUid, out _)
            ? origin.WithEntityId(gridUid, EntityManager)
            : new EntityCoordinates(_map.GetMap(originMap.MapId), originMap.Position);

        var fireball = Spawn(ProjectilePrototype, spawnCoords);
        _gun.ShootProjectile(
            fireball,
            direction,
            _physics.GetMapLinearVelocity(args.Performer),
            args.Performer,
            args.Performer,
            ProjectileSpeed);

        _psionics.LogPowerUsed(args.Performer, PowerName, 3, 5);
        args.Handled = true;
    }
}
