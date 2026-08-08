using Content.Server.Movement.Systems;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Movement;

[TestFixture]
[TestOf(typeof(SharedJetpackSystem))]
[TestOf(typeof(SharedMagbootsSystem))]
public sealed class JetpackMagbootsTest
{
    [Test]
    public async Task MagbootsAndJetpackAreMutuallyExclusive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var itemToggle = entMan.System<ItemToggleSystem>();
            var jetpackSystem = entMan.System<JetpackSystem>();

            var wearer = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var magboots = entMan.SpawnEntity("ClothingShoesBootsMag", testMap.GridCoords);
            var jetpack = entMan.SpawnEntity("JetpackMiniFilled", testMap.GridCoords);
            var magbootsComp = entMan.GetComponent<MagbootsComponent>(magboots);
            var jetpackComp = entMan.GetComponent<JetpackComponent>(jetpack);

            Assert.Multiple(() =>
            {
                Assert.That(inventory.TryEquip(wearer, magboots, "shoes", force: true));
                Assert.That(inventory.TryEquip(wearer, jetpack, "back", force: true));
                Assert.That(itemToggle.TryActivate(magboots, wearer));
                Assert.That(magbootsComp.Active);
            });

            jetpackSystem.SetEnabled(jetpack, jetpackComp, true, wearer);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.False);
                Assert.That(entMan.HasComponent<JetpackUserComponent>(wearer), Is.False);
            });

            Assert.That(itemToggle.TryDeactivate(magboots, wearer));
            jetpackSystem.SetEnabled(jetpack, jetpackComp, true, wearer);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack));
                Assert.That(entMan.HasComponent<JetpackUserComponent>(wearer));
                Assert.That(itemToggle.TryActivate(magboots, wearer), Is.False);
                Assert.That(magbootsComp.Active, Is.False);
            });

            // Pre-activating the boots while unworn must not bypass the lock when they are equipped.
            Assert.That(inventory.TryUnequip(wearer, "shoes", force: true));
            Assert.That(itemToggle.TryActivate(magboots, wearer));
            Assert.That(inventory.TryEquip(wearer, magboots, "shoes", force: true));
            Assert.That(magbootsComp.Active, Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
