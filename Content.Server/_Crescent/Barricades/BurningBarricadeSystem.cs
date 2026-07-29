using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._Crescent.Barricades;

[RegisterComponent, Access(typeof(BurningBarricadeSystem))]
public sealed partial class BurningBarricadeComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public float FireStacks = 0.35f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    [DataField]
    public float TileFireChance;

    public TimeSpan NextDamage;
}

/// <summary>
/// Damages and ignites living entities that remain in contact with a burning barricade.
/// </summary>
public sealed class BurningBarricadeSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BurningBarricadeComponent, FlammableComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var burning, out var flammable, out var physics))
        {
            if (!flammable.OnFire || _timing.CurTime < burning.NextDamage)
                continue;

            burning.NextDamage = _timing.CurTime + burning.Interval;

            if (burning.TileFireChance > 0f && _random.Prob(burning.TileFireChance))
                TrySpawnTileFire(uid);

            foreach (var target in _physics.GetContactingEntities(uid, physics, approximate: true))
            {
                if (!HasComp<MobStateComponent>(target))
                    continue;

                _damageable.TryChangeDamage(target, burning.Damage, interruptsDoAfters: false);

                if (!TryComp<FlammableComponent>(target, out var targetFlammable))
                    continue;

                _flammable.AdjustFireStacks(target, burning.FireStacks, targetFlammable);
                _flammable.Ignite(target, uid, targetFlammable);
            }
        }
    }

    private void TrySpawnTileFire(EntityUid source)
    {
        var direction = _random.Pick(new[]
        {
            Vector2.UnitX,
            -Vector2.UnitX,
            Vector2.UnitY,
            -Vector2.UnitY,
        });
        var coordinates = Transform(source).Coordinates
            .Offset(direction)
            .SnapToGrid(EntityManager, _mapSystem);

        if (_lookup.GetEntitiesInRange<CrescentTileFireComponent>(coordinates, 0.4f).Count != 0)
            return;

        Spawn("CrescentTileFire", coordinates);
    }
}
