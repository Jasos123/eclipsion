using Content.Server.Movement.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.IntegrationTests.Tests.Movement;

[TestFixture]
[TestOf(typeof(SharedJetpackSystem))]
public sealed class JetpackTest
{
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
