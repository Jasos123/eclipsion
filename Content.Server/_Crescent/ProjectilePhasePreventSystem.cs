using System.Numerics;
using Content.Shared._Crescent;
using Content.Server._Crescent.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

public sealed class ProjectilePhasePreventerSystem : EntitySystem
{
    [Dependency] private readonly PhysicsSystem _phys = default!;
    [Dependency] private readonly TransformSystem _trans = default!;
    [Dependency] private readonly SharedProjectileSystem _projectile = default!;
    [Dependency] private readonly ShipShieldsSystem _shipShields = default!;
    [Dependency] private readonly ILogManager _logs = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private readonly Dictionary<EntityUid, Entity<ProjectilePhasePreventComponent, ProjectileComponent>> _projectiles = new();

    private ISawmill _sawmill = default!;

    // xtra forgiveness beyond the projectile's exact movement distance. modify this if we ever raise tps opr have issues with phasing again
    private const float RaycastExtraDistance = 2f;

    // prevents tiny zero-length raycasts
    private const float MinimumTravelDistance = 0.001f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectilePhasePreventComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ProjectilePhasePreventComponent, ComponentShutdown>(OnShutdown);

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        _sawmill = _logs.GetSawmill("Phase-Prevention");
    }

    private void OnStartup(EntityUid uid, ProjectilePhasePreventComponent comp, ref ComponentStartup args)
    {
        if (!TryComp<ProjectileComponent>(uid, out var projectile))
        {
            _sawmill.Error($"Tried to initialize ProjectilePhasePreventComponent on entity without ProjectileComponent. Prototype: {MetaData(uid).EntityPrototype?.ID}");
            RemComp<ProjectilePhasePreventComponent>(uid);
            return;
        }

        comp.start = _trans.GetWorldPosition(uid);
        comp.mapId = _trans.GetMapId(uid);

        _projectiles[uid] = (uid, comp, projectile);
    }

    private void OnShutdown(EntityUid uid, ProjectilePhasePreventComponent comp, ref ComponentShutdown args)
    {
        _projectiles.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (owner, phase, projectile) in _projectiles.Values)
        {
            if (TerminatingOrDeleted(owner))
                continue;

            if (!_physicsQuery.TryGetComponent(owner, out var bulletPhysics))
                continue;

            if (!_fixturesQuery.TryGetComponent(owner, out var bulletFixtures))
                continue;

            if (bulletFixtures.Fixtures.Count == 0)
                continue;

            var currentPos = _trans.GetWorldPosition(owner);
            var currentMap = _trans.GetMapId(owner);

            // Never raycast across maps
            if (currentMap != phase.mapId)
            {
                phase.start = currentPos;
                phase.mapId = currentMap;
                continue;
            }

            var previousPos = phase.start;
            var delta = currentPos - previousPos;
            var distance = delta.Length();

            if (distance <= MinimumTravelDistance)
                continue;

            var direction = delta / distance;

            KeyValuePair<string, Fixture> bulletFixturePair = default;
            foreach (var kv in bulletFixtures.Fixtures) { bulletFixturePair = kv; break; }
            var bulletFixtureKey = bulletFixturePair.Key;

            var ignoredGrid = EntityUid.Invalid;

            if (projectile.Weapon != null &&
                _xformQuery.TryGetComponent(projectile.Weapon, out var weaponXform) &&
                weaponXform.GridUid != null)
            {
                ignoredGrid = weaponXform.GridUid.Value;
            }

            // PhasePrevention is a query-only layer used by soft shield bubbles. Normal collision masks do not
            // include it, so adding it here cannot make shields physically solid.
            var ray = new CollisionRay(previousPos,
                direction,
                phase.relevantBitmasks | (int) CollisionGroup.PhasePrevention);

            foreach (var hit in _phys.IntersectRay(
                         currentMap,
                         ray,
                         distance + RaycastExtraDistance,
                         projectile.Weapon,
                         false))
            {
                var hitEntity = hit.HitEntity;

                if (hitEntity == owner)
                    continue;

                if (projectile.IgnoreShooter && projectile.Shooter == hitEntity)
                    continue;

                if (projectile.IgnoredEntities.Contains(hitEntity))
                    continue;

                if (!_xformQuery.TryGetComponent(hitEntity, out var hitXform))
                    continue;

                if (projectile.IgnoreWeaponGrid &&
                    ignoredGrid != EntityUid.Invalid &&
                    hitXform.GridUid == ignoredGrid)
                {
                    continue;
                }

                // Rockets from the same shuttle pass through each other. A saturation launcher fires its whole
                // salvo from one muzzle, and this raycast reaches further than the gap between two shots, so
                // without this the burst detonates on itself. Keep scanning - a real target may be behind it.
                if (_projectile.IsFriendlyShipProjectile(owner, projectile, hitEntity))
                    continue;

                if (TryComp<ShipShieldComponent>(hitEntity, out var shield))
                {
                    if (_shipShields.TryQueueDeflection((hitEntity, shield), owner))
                        break;

                    // Shield-ignoring and same-grid rounds must keep scanning for a real target behind the bubble.
                    continue;
                }

                if (!_physicsQuery.TryGetComponent(hitEntity, out _))
                    continue;

                if (!_fixturesQuery.TryGetComponent(hitEntity, out var targetFixtures))
                    continue;

                if (targetFixtures.Fixtures.Count == 0)
                    continue;

                KeyValuePair<string, Fixture> targetFixturePair = default;
                foreach (var kv in targetFixtures.Fixtures) { targetFixturePair = kv; break; }

                var bulletEvent = new HullrotBulletHitEvent
                {
                    selfEntity = owner,
                    hitEntity = hitEntity,
                    selfFixtureKey = bulletFixtureKey,
                    targetFixture = targetFixturePair.Value,
                    targetFixtureKey = targetFixturePair.Key,
                    selfPhys = bulletPhysics
                };

                try
                {
                    RaiseLocalEvent(owner, ref bulletEvent, true);
                }
                catch (Exception e)
                {
                    _sawmill.Error($"Failed to raise phase-prevent hit event: {e}");
                }

                break;
            }

            phase.start = currentPos;
            phase.mapId = currentMap;
        }
    }
}
