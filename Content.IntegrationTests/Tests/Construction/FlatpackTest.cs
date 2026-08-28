using System.Numerics;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Construction;

[TestFixture]
public sealed class FlatpackTest
{
    [Test]
    public async Task FlatpackUnpacksOnAnEmptyTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entityManager = server.EntMan;

        EntityUid flatpack = default;

        await server.WaitAssertion(() =>
        {
            var targetCoordinates = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            var userCoordinates = map.GridCoords.Offset(new Vector2(0.5f, 1.5f));

            flatpack = entityManager.SpawnEntity("AmePartFlatpack", targetCoordinates);
            var multitool = entityManager.SpawnEntity("Multitool", userCoordinates);
            var user = entityManager.SpawnEntity("MobHuman", userCoordinates);

            var interact = new InteractUsingEvent(user, multitool, flatpack, targetCoordinates);
            entityManager.EventBus.RaiseLocalEvent(flatpack, interact);

            Assert.That(interact.Handled, Is.True);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.EntityExists(flatpack), Is.False,
                "The flatpack should be consumed when its tile has no blocker.");

            var shielding = entityManager.AllEntityQueryEnumerator<MetaDataComponent>();
            var spawned = false;
            while (shielding.MoveNext(out _, out var metadata))
            {
                if (metadata.EntityPrototype?.ID == "AmeShielding")
                {
                    spawned = true;
                    break;
                }
            }

            Assert.That(spawned, Is.True, "The flatpack should spawn its configured entity.");
        });

        await pair.CleanReturnAsync();
    }
}
