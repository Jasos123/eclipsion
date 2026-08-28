using Content.Server.Movement.Systems;
using Content.Shared.Clothing;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.Movement;

[TestFixture]
[TestOf(typeof(SharedJetpackSystem))]
public sealed class JetpackTest
{
    [Test]
    public async Task ActiveMagbootsSuspendJetpackOnGridAndResumeOffGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var inventory = entMan.System<InventorySystem>();
            var itemToggle = entMan.System<ItemToggleSystem>();
            var jetpackSystem = entMan.System<JetpackSystem>();
            var transform = entMan.System<SharedTransformSystem>();

            var wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var magboots = entMan.SpawnEntity("ClothingShoesBootsMag", map.GridCoords);
            var jetpack = entMan.SpawnEntity("JetpackMiniFilled", map.GridCoords);

            Assert.That(inventory.TryEquip(wearer, magboots, "shoes"), Is.True);
            Assert.That(inventory.TryEquip(wearer, jetpack, "back"), Is.True);
            Assert.That(itemToggle.TryActivate(magboots, wearer), Is.True);

            jetpackSystem.SetEnabled(jetpack, entMan.GetComponent<JetpackComponent>(jetpack), true, wearer);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True,
                    "Magboots must not switch the jetpack off.");
                Assert.That(itemToggle.IsActivated(magboots), Is.True,
                    "Enabling a jetpack must not switch the magboots off.");
                Assert.That(entMan.HasComponent<MagbootsUserComponent>(wearer), Is.True);
                Assert.That(entMan.HasComponent<RelayInputMoverComponent>(wearer), Is.False,
                    "The jetpack must not receive movement input while magboots are attached to a grid.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(wearer).BodyStatus, Is.EqualTo(BodyStatus.OnGround));
                Assert.That(jetpackSystem.IsProvidingThrust(jetpack), Is.False);
            });

            Assert.That(itemToggle.TryDeactivate(magboots, wearer), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True);
                Assert.That(entMan.HasComponent<MagbootsUserComponent>(wearer), Is.False);
                Assert.That(entMan.GetComponent<RelayInputMoverComponent>(wearer).RelayEntity, Is.EqualTo(jetpack),
                    "Turning magboots off must restore thrust without toggling the jetpack.");
                Assert.That(jetpackSystem.IsProvidingThrust(jetpack), Is.True);
            });

            Assert.That(itemToggle.TryActivate(magboots, wearer), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True);
                Assert.That(entMan.HasComponent<MagbootsUserComponent>(wearer), Is.True);
                Assert.That(entMan.HasComponent<RelayInputMoverComponent>(wearer), Is.False,
                    "Turning magboots on must suspend an already-enabled jetpack without switching it off.");
                Assert.That(jetpackSystem.IsProvidingThrust(jetpack), Is.False);
            });

            transform.SetCoordinates(wearer, new EntityCoordinates(map.MapUid, 2, 2));

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True);
                Assert.That(itemToggle.IsActivated(magboots), Is.True);
                Assert.That(entMan.GetComponent<RelayInputMoverComponent>(wearer).RelayEntity, Is.EqualTo(jetpack),
                    "The enabled jetpack must automatically resume when the wearer leaves the grid.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(wearer).BodyStatus, Is.EqualTo(BodyStatus.InAir));
                Assert.That(jetpackSystem.IsProvidingThrust(jetpack), Is.True);
            });

            transform.SetCoordinates(wearer, map.GridCoords);
            entMan.GetComponent<GravityComponent>(map.Grid).EnabledVV = true;

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True,
                    "Gravity must not switch off a jetpack while active magboots hold its wearer to the grid.");
                Assert.That(entMan.HasComponent<RelayInputMoverComponent>(wearer), Is.False);
                Assert.That(jetpackSystem.IsProvidingThrust(jetpack), Is.False);
            });

            jetpackSystem.SetEnabled(jetpack, entMan.GetComponent<JetpackComponent>(jetpack), false, wearer);
            var toggleJetpack = new ToggleJetpackEvent { Performer = wearer };
            entMan.EventBus.RaiseLocalEvent(jetpack, toggleJetpack);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True,
                    "Active magboots must allow a jetpack to enter standby under gravity.");
                Assert.That(entMan.HasComponent<RelayInputMoverComponent>(wearer), Is.False);
            });

            transform.SetCoordinates(wearer, new EntityCoordinates(map.MapUid, 3, 3));

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.True);
                Assert.That(entMan.GetComponent<RelayInputMoverComponent>(wearer).RelayEntity, Is.EqualTo(jetpack));
                Assert.That(jetpackSystem.IsProvidingThrust(jetpack), Is.True,
                    "The standby jetpack must resume after leaving a gravity-enabled grid.");
            });

            Assert.That(itemToggle.TryDeactivate(magboots, wearer), Is.True);
            transform.SetCoordinates(wearer, map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), Is.False,
                    "A jetpack must still switch off under gravity when active magboots are not holding its wearer.");
                Assert.That(entMan.HasComponent<JetpackUserComponent>(wearer), Is.False);
                Assert.That(entMan.HasComponent<RelayInputMoverComponent>(wearer), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SecondJetpackDoesNotBecomeActiveForSameUser()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var system = entMan.System<JetpackSystem>();
            var wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var first = entMan.SpawnEntity("JetpackMiniFilled", map.GridCoords);
            var second = entMan.SpawnEntity("JetpackMiniFilled", map.GridCoords);
            var firstComponent = entMan.GetComponent<JetpackComponent>(first);
            var secondComponent = entMan.GetComponent<JetpackComponent>(second);

            system.SetEnabled(first, firstComponent, true, wearer);
            system.SetEnabled(second, secondComponent, true, wearer);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(first), Is.True);
                Assert.That(entMan.HasComponent<ActiveJetpackComponent>(second), Is.False,
                    "A rejected second jetpack must not drain fuel or advertise itself as active.");
                Assert.That(entMan.GetComponent<JetpackUserComponent>(wearer).Jetpack, Is.EqualTo(first));
            });
        });

        await pair.CleanReturnAsync();
    }
}
