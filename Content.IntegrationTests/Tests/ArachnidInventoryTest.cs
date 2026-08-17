using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class ArachnidInventoryTest
{
    private static readonly ProtoId<InventoryTemplatePrototype> ArachnidInventory = "arachnid";

    [Test]
    public async Task SlotsUseExpectedHotbarGroups()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var template in prototypeManager.EnumeratePrototypes<InventoryTemplatePrototype>())
            {
                Assert.That(template.Slots.Select(slot => slot.Name), Is.Unique,
                    $"Inventory template {template.ID} has duplicate slot names.");
            }

            var inventory = prototypeManager.Index(ArachnidInventory);

            Assert.Multiple(() =>
            {
                Assert.That(inventory.Slots.Single(slot => slot.Name == "back").SlotGroup,
                    Is.EqualTo("SecondHotbar"));
                Assert.That(inventory.Slots.Single(slot => slot.Name == "suitstorage").SlotGroup,
                    Is.EqualTo("MainHotbar"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
