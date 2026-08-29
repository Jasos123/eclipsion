using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Materials;
using Content.Server.Stack;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Materials;

public sealed class MaterialReclaimerStackTest : InteractionTest
{
    [Test]
    public async Task ReclaimsCompositionForEveryItemInStack()
    {
        await SpawnTarget("MaterialReclaimer");

        await Server.WaitPost(() =>
        {
            var reclaimer = ToServer(Target!.Value);
            var item = SEntMan.SpawnEntity("CapacitorStockPart", Position(reclaimer));
            SEntMan.System<StackSystem>().SetCount(item, 4);

            var reclaimerComponent = SEntMan.GetComponent<MaterialReclaimerComponent>(reclaimer);
            SEntMan.System<MaterialReclaimerSystem>().Reclaim(reclaimer, item, component: reclaimerComponent);

            var storage = SEntMan.GetComponent<MaterialStorageComponent>(reclaimer);
            ProtoId<MaterialPrototype> steel = "Steel";
            ProtoId<MaterialPrototype> plastic = "Plastic";

            Assert.Multiple(() =>
            {
                Assert.That(storage.Storage[steel], Is.EqualTo(48));
                Assert.That(storage.Storage[plastic], Is.EqualTo(48));
            });
        });
    }
}
