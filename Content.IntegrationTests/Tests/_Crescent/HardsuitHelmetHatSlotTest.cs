using Content.Shared._Crescent.Clothing;
using Content.Shared.Clothing;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class HardsuitHelmetHatSlotTest
{
    [Test]
    public async Task HatCanBeSlottedAndIsIncludedInHelmetVisuals()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = pair;
        var map = await pair.CreateTestMap();

        EntityUid helmet = default;
        EntityUid hat = default;
        EntityUid armoredHelmet = default;
        EntityUid evaHelmet = default;
        EntityUid wearer = default;

        await server.WaitAssertion(() =>
        {
            helmet = server.EntMan.SpawnEntity("ClothingHeadHelmetHardsuitBasic", map.GridCoords);
            hat = server.EntMan.SpawnEntity("ClothingHeadHatTophat", map.GridCoords);
            armoredHelmet = server.EntMan.SpawnEntity("ClothingHeadHelmetBasic", map.GridCoords);
            evaHelmet = server.EntMan.SpawnEntity("ClothingHeadHelmetEVA", map.GridCoords);
            wearer = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);

            var slots = server.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(
                helmet,
                HardsuitHelmetHatSlotComponent.DefaultSlotId,
                hat,
                null), Is.True);
            Assert.That(slots.TryEject(
                helmet,
                HardsuitHelmetHatSlotComponent.DefaultSlotId,
                null,
                out var ejected), Is.True);
            Assert.That(ejected, Is.EqualTo(hat));
            Assert.That(slots.TryInsert(
                helmet,
                HardsuitHelmetHatSlotComponent.DefaultSlotId,
                armoredHelmet,
                null), Is.False);
            Assert.That(slots.TryInsert(
                helmet,
                HardsuitHelmetHatSlotComponent.DefaultSlotId,
                evaHelmet,
                null), Is.False);
            Assert.That(slots.TryInsert(
                helmet,
                HardsuitHelmetHatSlotComponent.DefaultSlotId,
                hat,
                null), Is.True);
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var clientHelmet = pair.ToClientUid(helmet);
            var clientWearer = pair.ToClientUid(wearer);
            var visuals = new GetEquipmentVisualsEvent(clientWearer, "head");
            client.EntMan.EventBus.RaiseLocalEvent(clientHelmet, visuals);

            Assert.That(visuals.Layers, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(visuals.Layers[^1].Item1,
                Does.StartWith("hardsuit-helmet-hat-"));
            Assert.That(visuals.Layers[^1].Item2.RsiPath,
                Is.EqualTo("/Textures/Clothing/Head/Hats/tophat.rsi"));
        });

        await pair.CleanReturnAsync();
    }
}
