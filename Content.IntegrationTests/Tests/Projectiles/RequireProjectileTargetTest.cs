using Content.Shared.Damage.Components;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests.Projectiles;

[TestFixture]
[TestOf(typeof(RequireProjectileTargetSystem))]
public sealed class RequireProjectileTargetTest
{
    private static readonly string[] LowStructurePrototypes =
    [
        // Furniture.
        "Table",
        "TableReinforced",
        "TableGlass",
        "TableWood",
        "TableCounterMetal",
        "Chair",
        "Stool",
        "SteelBench",

        // Other low structures which use the same targeting behavior.
        "hydroponicsTray",
        "hydroponicsMakeshiftTray",
        "MachineArtifactAnalyzer",
        "PowerCellRecharger",
        "filingCabinet",
        "filingCabinetTall",
        "filingCabinetDrawer",
        "PlasticFlapsClear",
        "SolarPanel",
        "DisposalUnit",
        "CrateGenericSteel",
        "ConveyorBelt",
    ];

    [Test]
    public async Task LowStructuresDoNotInterceptUntargetedProjectiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var shooter = entMan.SpawnEntity(null, map.GridCoords);
            var projectile = entMan.SpawnEntity("BulletPistol", map.GridCoords);
            var projectileComp = entMan.GetComponent<ProjectileComponent>(projectile);
            projectileComp.Shooter = shooter;

            using (Assert.EnterMultipleScope())
            {
                foreach (var prototype in LowStructurePrototypes)
                {
                    var structure = entMan.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(CollisionIsPrevented(structure), Is.True, prototype);
                }
            }

            bool CollisionIsPrevented(EntityUid structure)
            {
                Assert.That(entMan.HasComponent<RequireProjectileTargetComponent>(structure), Is.True);

                var collide = new PreventCollideEvent(
                    structure,
                    projectile,
                    entMan.GetComponent<PhysicsComponent>(structure),
                    entMan.GetComponent<PhysicsComponent>(projectile),
                    new Fixture(),
                    new Fixture());

                entMan.EventBus.RaiseLocalEvent(structure, ref collide);
                return collide.Cancelled;
            }
        });

        await pair.CleanReturnAsync();
    }
}
