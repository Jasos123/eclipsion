using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._Crescent.Barricades;

[RegisterComponent, Access(typeof(CrescentTileFireSystem))]
public sealed partial class CrescentFireOnTriggerComponent : Component
{
    [DataField]
    public float Radius = 2.5f;

    [DataField]
    public int Count = 9;
}

[RegisterComponent, Access(typeof(CrescentTileFireSystem))]
public sealed partial class CrescentFlameProjectileComponent : Component
{
    [DataField]
    public TimeSpan SpawnDelay = TimeSpan.FromSeconds(0.12);

    [DataField]
    public TimeSpan TrailInterval = TimeSpan.FromSeconds(0.1);

    [DataField]
    public float ImpactRadius = 1.2f;

    [DataField]
    public int ImpactFireCount = 5;

    public TimeSpan NextTrailSpawn;
    public bool ImpactFireSpawned;
}

[RegisterComponent, Access(typeof(CrescentTileFireSystem))]
public sealed partial class CrescentTileFireComponent : Component
{
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public float IgniteStacks = 0.75f;

    [DataField]
    public float MinimumOxygenMoles = 0.5f;

    [DataField]
    public TimeSpan VacuumExtinguishDelay = TimeSpan.FromSeconds(1.5);

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    public TimeSpan DeleteAt;
    public TimeSpan NextTick;
    public TimeSpan? OxygenStarvedSince;
}

/// <summary>
/// A bounded RMC-style floor fire: it burns and ignites mobs, spreads to adjacent tiles,
/// expires over time, and is removed when an extinguisher extinguishes its Flammable component.
/// </summary>
public sealed class CrescentTileFireSystem : EntitySystem
{
    private static readonly string[] BurntDecals = ["burnt1", "burnt2", "burnt3", "burnt4"];

    /// <summary>Anything solid enough to stop fire spreading onto the tile behind it.</summary>
    private const CollisionGroup FireBlockingMask = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;

    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrescentTileFireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CrescentFireOnTriggerComponent, TriggerEvent>(OnFireTrigger);
        SubscribeLocalEvent<CrescentFlameProjectileComponent, MapInitEvent>(OnFlameProjectileMapInit);
        SubscribeLocalEvent<CrescentFlameProjectileComponent, ProjectileHitEvent>(OnFlameProjectileHit);
    }

    private void OnMapInit(Entity<CrescentTileFireComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.DeleteAt = _timing.CurTime + ent.Comp.Lifetime;
        ent.Comp.NextTick = _timing.CurTime;

        if (TryComp<FlammableComponent>(ent, out var flammable))
        {
            _flammable.SetFireStacks(ent, flammable.MaximumFireStacks, flammable);
            _flammable.Ignite(ent, ent, flammable);
        }
    }

    private void OnFlameProjectileMapInit(Entity<CrescentFlameProjectileComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTrailSpawn = _timing.CurTime + ent.Comp.SpawnDelay;
    }

    private void OnFlameProjectileHit(Entity<CrescentFlameProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (ent.Comp.ImpactFireSpawned || Deleted(ent))
            return;

        ent.Comp.ImpactFireSpawned = true;
        SpawnFirePatch(
            Transform(ent).Coordinates,
            ent.Comp.ImpactRadius,
            ent.Comp.ImpactFireCount,
            GetApproachDirection(ent));
    }

    /// <summary>
    /// World-space direction the projectile arrived from, used to back the impact patch off a wall.
    /// </summary>
    private Vector2? GetApproachDirection(EntityUid uid)
    {
        if (TryComp<PhysicsComponent>(uid, out var physics) && physics.LinearVelocity.LengthSquared() > 0f)
            return -physics.LinearVelocity;

        var forward = _transform.GetWorldRotation(uid).ToWorldVec();
        return forward.LengthSquared() > 0f ? -forward : null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var projectileQuery = EntityQueryEnumerator<CrescentFlameProjectileComponent>();
        while (projectileQuery.MoveNext(out var projectileUid, out var projectile))
        {
            if (_timing.CurTime < projectile.NextTrailSpawn)
                continue;

            projectile.NextTrailSpawn = _timing.CurTime + projectile.TrailInterval;
            TrySpawnProjectileTrailFire((projectileUid, projectile));
        }

        var query = EntityQueryEnumerator<CrescentTileFireComponent, FlammableComponent>();
        while (query.MoveNext(out var uid, out var fire, out var flammable))
        {
            if (_timing.CurTime >= fire.DeleteAt || !flammable.OnFire)
            {
                LeaveBurntDecal(uid);
                QueueDel(uid);
                continue;
            }

            if (!HasEnoughOxygen(uid, fire))
            {
                fire.OxygenStarvedSince ??= _timing.CurTime;
                if (_timing.CurTime - fire.OxygenStarvedSince >= fire.VacuumExtinguishDelay)
                {
                    _flammable.Extinguish(uid, flammable);
                    LeaveBurntDecal(uid);
                    QueueDel(uid);
                }
                continue;
            }

            fire.OxygenStarvedSince = null;

            if (_timing.CurTime >= fire.NextTick)
            {
                fire.NextTick = _timing.CurTime + fire.TickInterval;
                BurnNearby(uid, fire);
            }
        }
    }

    private void TrySpawnProjectileTrailFire(Entity<CrescentFlameProjectileComponent> projectile)
    {
        if (Deleted(projectile) || Transform(projectile).MapID == MapId.Nullspace)
            return;

        var mapPos = _transform.GetMapCoordinates(projectile.Owner);
        if (!_mapSystem.TryFindGridAt(mapPos, out var gridUid, out var grid))
            return;

        var coordinates = _mapSystem.MapToGrid(gridUid, mapPos).SnapToGrid(EntityManager, _mapSystem);

        // Never drop a trail tile inside a wall the round hasn't actually passed.
        if (IsFireBlocked(gridUid, grid, _mapSystem.TileIndicesFor(gridUid, grid, coordinates)))
            return;

        if (_lookup.GetEntitiesInRange<CrescentTileFireComponent>(coordinates, 0.4f).Count == 0)
            Spawn("CrescentTileFire", coordinates);
    }

    private void OnFireTrigger(Entity<CrescentFireOnTriggerComponent> ent, ref TriggerEvent args)
    {
        SpawnFirePatch(Transform(ent).Coordinates, ent.Comp.Radius, ent.Comp.Count);
    }

    /// <summary>
    /// Scatters floor fire around <paramref name="epicenter"/>. Fire only lands on tiles it could
    /// actually reach across open floor, so a flamer emptied into a wall burns in front of it
    /// instead of spraying flame into the room on the other side.
    /// </summary>
    /// <param name="approach">
    /// Optional world-space direction the fire arrived from. Used to step the epicentre back out of
    /// a wall when a projectile detonated against one.
    /// </param>
    private void SpawnFirePatch(EntityCoordinates epicenter, float radius, int count, Vector2? approach = null)
    {
        var mapEpicenter = _transform.ToMapCoordinates(epicenter);
        if (!_mapSystem.TryFindGridAt(mapEpicenter, out var gridUid, out var grid))
            return;

        var gridEpicenter = _mapSystem.MapToGrid(gridUid, mapEpicenter);
        var origin = _mapSystem.TileIndicesFor(gridUid, grid, gridEpicenter);

        if (!TryGetPatchSeed(gridUid, grid, origin, approach, out var seed))
            return;

        // If the shot ended inside a wall, recentre the splash on the tile in front of it.
        if (seed != origin)
            gridEpicenter = _mapSystem.GridTileToLocal(gridUid, grid, seed);

        var reachable = GetReachableTiles(gridUid, grid, seed, radius);

        for (var i = 0; i < count; i++)
        {
            var offset = i == 0 ? Vector2.Zero : _random.NextVector2(radius);
            var coordinates = gridEpicenter
                .Offset(offset)
                .SnapToGrid(EntityManager, _mapSystem);

            if (!reachable.Contains(_mapSystem.TileIndicesFor(gridUid, grid, coordinates)))
                continue;

            if (_lookup.GetEntitiesInRange<CrescentTileFireComponent>(coordinates, 0.4f).Count != 0)
                continue;

            Spawn("CrescentTileFire", coordinates);
        }
    }

    /// <summary>
    /// Picks the tile the patch actually grows out of. Normally the epicentre itself, but a round
    /// that stopped against a wall has to fall back to the tile it flew in from.
    /// </summary>
    private bool TryGetPatchSeed(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i origin,
        Vector2? approach,
        out Vector2i seed)
    {
        seed = origin;

        if (!IsFireBlocked(gridUid, grid, origin))
            return true;

        if (approach is not { } worldDir || worldDir.LengthSquared() <= 0f)
            return false;

        // The direction is in world space; the grid it lands on may be rotated (ships are).
        var localDir = (-_transform.GetWorldRotation(gridUid)).RotateVec(worldDir);
        var step = MathF.Abs(localDir.X) >= MathF.Abs(localDir.Y)
            ? new Vector2i(MathF.Sign(localDir.X), 0)
            : new Vector2i(0, MathF.Sign(localDir.Y));

        seed = origin + step;
        return !IsFireBlocked(gridUid, grid, seed);
    }

    /// <summary>
    /// Flood fills open floor out from <paramref name="seed"/>, so walls bound the patch.
    /// </summary>
    private HashSet<Vector2i> GetReachableTiles(EntityUid gridUid, MapGridComponent grid, Vector2i seed, float radius)
    {
        // Bound by the same Euclidean radius the scatter samples, not by a step count: a step count is a
        // Manhattan bound, so it cuts the corners off the disc. (2,2) sits inside a radius of 3 but is
        // four steps away, and those tiles were being silently dropped from every patch.
        var radiusSquared = Math.Max(1f, radius * radius);
        var reachable = new HashSet<Vector2i>();
        var visited = new HashSet<Vector2i> { seed };
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(seed);

        while (frontier.TryDequeue(out var indices))
        {
            if (IsFireBlocked(gridUid, grid, indices))
                continue;

            reachable.Add(indices);

            foreach (var offset in Neighbours)
            {
                var next = indices + offset;
                var delta = next - seed;

                if (delta.X * delta.X + delta.Y * delta.Y > radiusSquared)
                    continue;

                if (visited.Add(next))
                    frontier.Enqueue(next);
            }
        }

        return reachable;
    }

    /// <summary>
    /// True if fire can neither sit on nor travel through this tile - open space, or something solid
    /// enough to block line of sight such as a wall, window or shut airlock.
    /// </summary>
    public bool IsFireBlocked(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        if (!_mapSystem.TryGetTile(grid, indices, out var tile) || tile.IsEmpty)
            return true;

        return _turf.IsTileBlocked(gridUid, indices, FireBlockingMask, grid);
    }

    private bool HasEnoughOxygen(EntityUid uid, CrescentTileFireComponent fire)
    {
        var mixture = _atmosphere.GetTileMixture((uid, Transform(uid)));
        return mixture != null && mixture.GetMoles(Gas.Oxygen) >= fire.MinimumOxygenMoles;
    }

    private void BurnNearby(EntityUid fireUid, CrescentTileFireComponent fire)
    {
        var coordinates = Transform(fireUid).Coordinates;
        foreach (var (target, _) in _lookup.GetEntitiesInRange<MobStateComponent>(coordinates, 0.55f))
        {
            _damageable.TryChangeDamage(target, fire.Damage, interruptsDoAfters: false);

            if (!TryComp<FlammableComponent>(target, out var targetFlammable))
                continue;

            _flammable.AdjustFireStacks(target, fire.IgniteStacks, targetFlammable);
            _flammable.Ignite(target, fireUid, targetFlammable);
        }
    }

    private void LeaveBurntDecal(EntityUid fireUid)
    {
        if (!Transform(fireUid).Coordinates.TryGetTileRef(out var tile, EntityManager, _mapSystem))
            return;

        var tileCenter = new Vector2(tile.Value.GridIndices.X + 0.5f, tile.Value.GridIndices.Y + 0.5f);
        foreach (var (_, decal) in _decals.GetDecalsInRange(tile.Value.GridUid, tileCenter, 0.5f))
        {
            if (Array.IndexOf(BurntDecals, decal.Id) != -1)
                return;
        }

        var coordinates = new EntityCoordinates(tile.Value.GridUid, tile.Value.GridIndices);
        _decals.TryAddDecal(_random.Pick(BurntDecals), coordinates, out _, cleanable: true);
    }

}
