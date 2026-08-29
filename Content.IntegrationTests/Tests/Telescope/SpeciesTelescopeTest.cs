using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Telescope;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Telescope;

[TestFixture]
[TestOf(typeof(TelescopeComponent))]
public sealed class SpeciesTelescopeTest
{
    [Test]
    public async Task AllSpeciesHaveTelescopeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var telescopeName = componentFactory.GetComponentName(typeof(TelescopeComponent));

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in prototypeManager.EnumeratePrototypes<SpeciesPrototype>())
                {
                    var entity = prototypeManager.Index(species.Prototype);
                    Assert.That(
                        entity.Components.ContainsKey(telescopeName),
                        Is.True,
                        $"Species {species.ID} ({species.Prototype}) does not have a Telescope component");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
