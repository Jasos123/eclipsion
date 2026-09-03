using Content.Shared._Crescent.HardsuitInjection;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class HardsuitInjectorTest
{
    [Test]
    public async Task AcceptsSupportedSourcesAndInjectsFromChemistryBottle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var slots = server.System<ItemSlotsSystem>();
            var inventory = server.System<InventorySystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();

            var suit = entMan.SpawnEntity("ClothingOuterHardsuitBasic", map.GridCoords);
            var wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var medipen = entMan.SpawnEntity("EmergencyMedipen", map.GridCoords);
            var hypospray = entMan.SpawnEntity("Hypospray", map.GridCoords);
            var bottle = entMan.SpawnEntity("EpinephrineChemistryBottle", map.GridCoords);
            var beaker = entMan.SpawnEntity("Beaker", map.GridCoords);
            const string slot = HardsuitInjectorComponent.SlotOneId;

            Assert.That(slots.TryInsert(suit, slot, medipen, null), Is.True);
            Assert.That(slots.TryEject(suit, slot, null, out _), Is.True);
            Assert.That(slots.TryInsert(suit, slot, hypospray, null), Is.True);
            Assert.That(slots.TryEject(suit, slot, null, out _), Is.True);
            Assert.That(slots.TryInsert(suit, slot, beaker, null), Is.False);
            Assert.That(slots.TryInsert(suit, slot, bottle, null), Is.True);
            Assert.That(inventory.TryEquip(wearer, suit, "outerClothing", silent: true, force: true), Is.True);

            Assert.That(solutions.TryGetRefillableSolution(bottle, out _, out var bottleSolution), Is.True);
            Assert.That(bottleSolution!.Volume, Is.EqualTo(FixedPoint2.New(30)));

            var ev = new HardsuitInjectActionEvent
            {
                Performer = wearer,
                Slot = slot,
            };
            entMan.EventBus.RaiseLocalEvent(suit, ev);

            Assert.That(ev.Handled, Is.True);
            Assert.That(bottleSolution.Volume, Is.EqualTo(FixedPoint2.New(15)));
            Assert.That(solutions.TryGetInjectableSolution(wearer, out _, out var bloodstream), Is.True);
            Assert.That(
                bloodstream!.GetTotalPrototypeQuantity("Epinephrine"),
                Is.EqualTo(FixedPoint2.New(15)));
        });

        await pair.CleanReturnAsync();
    }
}
