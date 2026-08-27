using System.Numerics;
using Content.Server.Weapons.Melee;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._NF;

[TestFixture]
public sealed class GolemCombatTest
{
    [Test]
    public async Task OreGolemCanBeDamagedAndDestroyedByMeleeAndProjectiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid attacker = default;
        EntityUid golem = default;
        EntityUid bullet = default;
        EntityUid meleeFinisherGolem = default;
        EntityUid projectileFinisherGolem = default;
        EntityUid finisherBullet = default;
        var damageAfterMelee = 0f;

        await server.WaitAssertion(() =>
        {
            attacker = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-1f, 0f)));
            golem = entMan.SpawnEntity("MobIronGolem", map.GridCoords);

            var combat = entMan.System<SharedCombatModeSystem>();
            var melee = entMan.System<MeleeWeaponSystem>();
            var physics = entMan.System<SharedPhysicsSystem>();
            var damage = entMan.System<DamageableSystem>();
            var mobState = entMan.System<MobStateSystem>();
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var weapon = entMan.GetComponent<MeleeWeaponComponent>(attacker);
            var damageable = entMan.GetComponent<DamageableComponent>(golem);
            var projectileTarget = entMan.GetComponent<RequireProjectileTargetComponent>(golem);

            Assert.That(projectileTarget.Active, Is.False);

            combat.SetInCombatMode(attacker, true);
            Assert.That(melee.AttemptLightAttack(attacker, attacker, weapon, golem), Is.True);
            Assert.That(damageable.TotalDamage > 0, Is.True);
            damageAfterMelee = (float) damageable.TotalDamage;

            var ray = new CollisionRay(
                new Vector2(-1f, 0f),
                Vector2.UnitX,
                (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable));
            var hits = physics.IntersectRay(map.MapId, ray, 2f, attacker, false).ToList();

            Assert.That(hits.Select(hit => hit.HitEntity), Does.Contain(golem));

            bullet = entMan.SpawnEntity("BulletPistol", map.GridCoords.Offset(new Vector2(-1f, 0f)));
            entMan.System<GunSystem>().ShootProjectile(bullet, Vector2.UnitX, Vector2.Zero, attacker, attacker, 10f);

            // Regression setup: golems used to enter Dead at 120 damage even though their destruction
            // threshold is 150. Dead mobs have collisions disabled, making the final damage impossible.
            var almostDestroyed = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), 149);

            var meleeAttacker = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-1f, 2f)));
            meleeFinisherGolem = entMan.SpawnEntity("MobIronGolem", map.GridCoords.Offset(new Vector2(0f, 2f)));
            damage.TryChangeDamage(meleeFinisherGolem, almostDestroyed);
            Assert.That(mobState.IsDead(meleeFinisherGolem), Is.False);
            Assert.That(entMan.GetComponent<PhysicsComponent>(meleeFinisherGolem).CanCollide, Is.True);

            var meleeFinisher = entMan.GetComponent<MeleeWeaponComponent>(meleeAttacker);
            combat.SetInCombatMode(meleeAttacker, true);
            Assert.That(melee.AttemptLightAttack(meleeAttacker, meleeAttacker, meleeFinisher, meleeFinisherGolem), Is.True);

            projectileFinisherGolem = entMan.SpawnEntity("MobIronGolem", map.GridCoords.Offset(new Vector2(0f, 4f)));
            damage.TryChangeDamage(projectileFinisherGolem, almostDestroyed);
            Assert.That(mobState.IsDead(projectileFinisherGolem), Is.False);
            Assert.That(entMan.GetComponent<PhysicsComponent>(projectileFinisherGolem).CanCollide, Is.True);

            finisherBullet = entMan.SpawnEntity("BulletPistol", map.GridCoords.Offset(new Vector2(-1f, 4f)));
            entMan.System<GunSystem>().ShootProjectile(finisherBullet, Vector2.UnitX, Vector2.Zero, attacker, attacker, 10f);
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var damageable = entMan.GetComponent<DamageableComponent>(golem);
            Assert.That((float) damageable.TotalDamage, Is.GreaterThan(damageAfterMelee));
            Assert.That(entMan.Deleted(bullet), Is.True);
            Assert.That(entMan.Deleted(meleeFinisherGolem), Is.True);
            Assert.That(entMan.Deleted(projectileFinisherGolem), Is.True);
            Assert.That(entMan.Deleted(finisherBullet), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
