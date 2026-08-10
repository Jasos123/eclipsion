using Content.Server._Crescent.TemporalDesync;
using Content.Server.Polymorph.Components;
using Content.Shared._Crescent.Overlays;
using Content.Shared._Crescent.SpaceBiomes;
using Content.Shared._Crescent.TemporalDesync;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
[TestOf(typeof(TemporalDesyncSystem))]
public sealed class TemporalDesyncTest
{
    [Test]
    public async Task UpdateUsesElapsedSecondsAndOnlyPolymorphsOnThresholdCrossing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var system = server.System<TemporalDesyncSystem>();
            var entity = entMan.SpawnEntity(null, map.GridCoords);
            var desync = entMan.AddComponent<TemporalDesyncComponent>(entity);
            desync.DesyncLevel = 0.25f;
            var tracker = entMan.AddComponent<SpaceBiomeTrackerComponent>(entity);
            tracker.Biome = "default";
            var resistance = entMan.AddComponent<DesyncResistanceComponent>(entity);
            resistance.ResistanceMultiplier = 1f;

            system.Update(0.5f);

            Assert.Multiple(() =>
            {
                Assert.That(desync.DesyncLevel, Is.EqualTo(0.2501f).Within(0.0000001f));
                Assert.That(entMan.GetComponent<StaticOverlayComponent>(entity).AdditionLevel,
                    Is.EqualTo(desync.DesyncLevel));
            });

            // Being loaded or reverted at the cap is not a new threshold crossing.
            desync.DesyncLevel = 1f;
            system.Update(1f);

            Assert.That(entMan.Count<PolymorphedEntityComponent>(), Is.Zero);
        });

        await pair.CleanReturnAsync();
    }
}
