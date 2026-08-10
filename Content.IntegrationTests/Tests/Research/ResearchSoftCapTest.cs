using Content.Server.Research.Systems;
using Content.Shared.Research.Components;

namespace Content.IntegrationTests.Tests.Research;

[TestFixture]
[TestOf(typeof(ResearchSystem))]
public sealed class ResearchSoftCapTest
{
    [Test]
    public async Task SyncCopiesAuthoritativeServerSoftCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var client = entMan.SpawnEntity(null, map.GridCoords);
            var researchServer = entMan.SpawnEntity(null, map.GridCoords);
            var clientDatabase = entMan.AddComponent<TechnologyDatabaseComponent>(client);
            var serverDatabase = entMan.AddComponent<TechnologyDatabaseComponent>(researchServer);
            var serverComponent = entMan.AddComponent<ResearchServerComponent>(researchServer);
            serverComponent.CurrentSoftCapMultiplier = 2.5f;

            entMan.System<ResearchSystem>().Sync(client, researchServer, clientDatabase, serverDatabase);

            Assert.That(clientDatabase.SoftCapMultiplier, Is.EqualTo(2.5f));
        });

        await pair.CleanReturnAsync();
    }
}
