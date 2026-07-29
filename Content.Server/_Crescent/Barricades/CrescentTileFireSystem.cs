using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
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
    public TimeSpan SpreadInterval = TimeSpan.FromSeconds(3);

    [DataField]
    public float SpreadChance = 0.12f;

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
    public TimeSpan NextSpread;
    public TimeSpan? OxygenStarvedSince;
}

/// <summary>
/// A bounded RMC-style floor fire: it burns and ignites mobs, spreads to adjacent tiles,
/// expires over time, and is removed when an extinguisher extinguishes its Flammable component.
/// </summary>
public sealed class CrescentTileFireSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly Vector2[] CardinalDirections =
    [
        Vector2.UnitX,
        -Vector2.UnitX,
        Vector2.UnitY,
        -Vector2.UnitY,
    ];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrescentTileFireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CrescentFireOnTriggerComponent, TriggerEvent>(OnFireTrigger);
        SubscribeLocalEvent<CrescentFlameProjectileComponent, MapInitEvent>(OnFlameProjectileMapInit);
        SubscribeLocalEvent<CrescentFlameProjectileComponent, ProjectileHitEvent>(OnFlameProjectileHit);
        SubscribeLocalEvent<ExplosionFireEvent>(OnExplosionFire);
    }

    private void OnMapInit(Entity<CrescentTileFireComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.DeleteAt = _timing.CurTime + ent.Comp.Lifetime;
        ent.Comp.NextTick = _timing.CurTime;
        ent.Comp.NextSpread = _timing.CurTime + ent.Comp.SpreadInterval;

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
            ent.Comp.ImpactFireCount);
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
                QueueDel(uid);
                continue;
            }

            if (!HasEnoughOxygen(uid, fire))
            {
                fire.OxygenStarvedSince ??= _timing.CurTime;
                if (_timing.CurTime - fire.OxygenStarvedSince >= fire.VacuumExtinguishDelay)
                {
                    _flammable.Extinguish(uid, flammable);
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

            if (_timing.CurTime >= fire.NextSpread)
            {
                fire.NextSpread = _timing.CurTime + fire.SpreadInterval;
                if (_random.Prob(fire.SpreadChance))
                    TrySpread((uid, fire));
            }
        }
    }

    private void TrySpawnProjectileTrailFire(Entity<CrescentFlameProjectileComponent> projectile)
    {
        if (Deleted(projectile) || Transform(projectile).MapID == MapId.Nullspace)
            return;

        var coordinates = Transform(projectile).Coordinates
            .SnapToGrid(EntityManager, _mapSystem);

        if (_lookup.GetEntitiesInRange<CrescentTileFireComponent>(coordinates, 0.4f).Count == 0)
            Spawn("CrescentTileFire", coordinates);
    }

    private void OnExplosionFire(ExplosionFireEvent args)
    {
        var count = Math.Clamp((int) MathF.Ceiling(args.Radius * args.FireStacks), 2, 16);
        SpawnFirePatch(args.Epicenter, args.Radius, count);
    }

    private void OnFireTrigger(Entity<CrescentFireOnTriggerComponent> ent, ref TriggerEvent args)
    {
        SpawnFirePatch(Transform(ent).Coordinates, ent.Comp.Radius, ent.Comp.Count);
    }

    private void SpawnFirePatch(EntityCoordinates epicenter, float radius, int count)
    {
        var mapEpicenter = _transform.ToMapCoordinates(epicenter);
        if (!_mapSystem.TryFindGridAt(mapEpicenter, out var gridUid, out _))
            return;

        var gridEpicenter = _mapSystem.MapToGrid(gridUid, mapEpicenter);
        for (var i = 0; i < count; i++)
        {
            var offset = i == 0 ? Vector2.Zero : _random.NextVector2(radius);
            var coordinates = gridEpicenter
                .Offset(offset)
                .SnapToGrid(EntityManager, _mapSystem);
            if (_lookup.GetEntitiesInRange<CrescentTileFireComponent>(coordinates, 0.4f).Count != 0)
                continue;

            Spawn("CrescentTileFire", coordinates);
        }
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

    private void TrySpread(Entity<CrescentTileFireComponent> source)
    {
        var coordinates = Transform(source).Coordinates
            .Offset(_random.Pick(CardinalDirections))
            .SnapToGrid(EntityManager, _mapSystem);
        if (_lookup.GetEntitiesInRange<CrescentTileFireComponent>(coordinates, 0.4f).Count != 0)
            return;

        var spreadFire = Spawn("CrescentTileFire", coordinates);
        if (TryComp(spreadFire, out CrescentTileFireComponent? spreadFireComponent))
            spreadFireComponent.DeleteAt = source.Comp.DeleteAt;
    }
}
