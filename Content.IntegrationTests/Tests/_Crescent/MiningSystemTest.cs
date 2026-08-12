using System.Linq;
using Content.Server.Gatherable;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class MiningSystemTest
{
    private const string StackRock = "CrescentMiningTestStackRock";
    private const string StackOre = "SteelOre1Unprocessed";
    private const string NonStackRock = "CrescentMiningTestNonStackRock";
    private const string NonStackDrop = "CrescentMiningTestNonStackDrop";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ore
  id: CrescentMiningTestStackOre
  oreEntity: SteelOre1Unprocessed
  minOreYield: 4
  maxOreYield: 4

- type: ore
  id: CrescentMiningTestNonStackOre
  oreEntity: CrescentMiningTestNonStackDrop
  minOreYield: 3
  maxOreYield: 3

- type: entity
  id: CrescentMiningTestStackRock
  parent: WallRock
  components:
  - type: OreVein
    currentOre: CrescentMiningTestStackOre

- type: entity
  id: CrescentMiningTestNonStackRock
  parent: WallRock
  components:
  - type: OreVein
    currentOre: CrescentMiningTestNonStackOre

- type: entity
  id: CrescentMiningTestNonStackDrop
";

    [Test]
    public async Task RepeatedGatherProducesOneOreStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var rock = entMan.SpawnEntity(StackRock, testMap.GridCoords);
            var gather = entMan.System<GatherableSystem>();

            gather.Gather(rock);
            gather.Gather(rock);
        });

        await server.WaitAssertion(() =>
        {
            var ores = entMan.System<EntityLookupSystem>()
                .GetEntitiesInRange(testMap.GridCoords, 1f, LookupFlags.All | LookupFlags.Approximate)
                .Where(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == StackOre)
                .ToList();

            Assert.That(ores, Has.Count.EqualTo(1), "A queued rock must not be gathered a second time.");
            Assert.That(entMan.GetComponent<StackComponent>(ores[0]).Count, Is.EqualTo(4),
                "The ore yield must be preserved in the single stack.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NonStackMiningOutputsRemainSeparate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var rock = entMan.SpawnEntity(NonStackRock, testMap.GridCoords);
            entMan.System<GatherableSystem>().Gather(rock);
        });

        await server.WaitAssertion(() =>
        {
            var drops = entMan.System<EntityLookupSystem>()
                .GetEntitiesInRange(testMap.GridCoords, 1f, LookupFlags.All | LookupFlags.Approximate)
                .Count(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == NonStackDrop);

            Assert.That(drops, Is.EqualTo(3), "Non-stack mining outputs must preserve their entity count.");
        });

        await pair.CleanReturnAsync();
    }
}
