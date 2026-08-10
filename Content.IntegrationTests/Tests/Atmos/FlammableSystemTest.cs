using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Atmos;

[TestFixture]
[TestOf(typeof(FlammableSystem))]
public sealed class FlammableSystemTest
{
    [Test]
    public async Task IgnitingThroughFireStacksAddsTickMarker()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var source = entMan.SpawnEntity(null, map.GridCoords);
            var target = entMan.SpawnEntity(null, map.GridCoords);
            entMan.AddComponent<AppearanceComponent>(target);
            var flammable = entMan.AddComponent<FlammableComponent>(target);
            var system = entMan.System<FlammableSystem>();

            system.SetFireStacks(target, 1f, flammable, ignite: true, ignitionSource: source);

            Assert.Multiple(() =>
            {
                Assert.That(flammable.OnFire, Is.True);
                Assert.That(entMan.HasComponent<OnFireComponent>(target), Is.True,
                    "Contact ignition must enroll the entity in the fire update loop.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
