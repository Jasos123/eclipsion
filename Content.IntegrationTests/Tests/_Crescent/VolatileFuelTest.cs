#nullable enable
using System.Linq;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.EntityEffects.Effects;
using Content.Server.Explosion.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class VolatileFuelTest
{
    [Test]
    public async Task FuelsIgniteWhatTheyTouch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        Assert.Multiple(() =>
        {
            foreach (var reagentId in new[] { "BoriaticFuel", "AmeFuel" })
            {
                var reagent = protoMan.Index<ReagentPrototype>(reagentId);

                Assert.That(reagent.ReactiveEffects, Is.Not.Null, $"{reagentId} must react on contact.");
                Assert.That(reagent.ReactiveEffects!.TryGetValue("Flammable", out var flammable), Is.True,
                    $"{reagentId} must be in the Flammable reactive group.");
                Assert.That(flammable!.Methods, Contains.Item(ReactionMethod.Touch));

                Assert.That(flammable.Effects.Any(e => e is Ignite), Is.True,
                    $"{reagentId} must actually ignite what it is splashed on, not just add fire stacks.");
                Assert.That(flammable.Effects.Any(e => e is FlammableReaction), Is.True,
                    $"{reagentId} must still add fire stacks - restating reactiveEffects drops the parent's.");

                Assert.That(reagent.TileReactions.Any(), Is.True,
                    $"{reagentId} must react with the tile it is spilled on.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelContainersCookOffScaledByContents()
    {
        // Prototype and the solution used to scale its explosion.
        var containers = new (string Proto, string Solution)[]
        {
            ("JugBoriaticFuel", "beaker"),
            ("FuelCanisterBoriaticT2", "beaker"),
            ("FuelCanisterBoriaticT3", "beaker"),
            ("BoriaticFuelTank", "tank"),
            ("AmeFuelTank", "tank"),
            ("AmeJar", "tank"),
            ("AmeJarT2", "tank"),
            ("AmeJarT3", "tank"),
            ("FuelExtractor", "buffer"),
            ("BoriaticGeneratorHercules", "tank"),
            ("BoriaticGeneratorEinstein", "tank"),
            ("BoriaticGeneratorTitan", "tank"),
        };

        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
        var compFactory = pair.Server.ResolveDependency<IComponentFactory>();

        Assert.Multiple(() =>
        {
            foreach (var (protoId, solution) in containers)
            {
                var proto = protoMan.Index<EntityPrototype>(protoId);

                Assert.That(proto.TryGetComponent<ExplosiveComponent>(out var explosive, compFactory), Is.True,
                    $"{protoId} holds raw fuel and must have an explosion to trigger.");
                Assert.That(explosive!.TotalIntensity, Is.GreaterThan(0f));

                Assert.That(proto.TryGetComponent<DestructibleComponent>(out var destructible, compFactory), Is.True,
                    $"{protoId} must be destructible or nothing can ever set the explosion off.");

                var behaviors = destructible!.Thresholds.SelectMany(t => t.Behaviors).ToList();
                var scaled = behaviors.OfType<SolutionExplosionBehavior>().ToList();

                Assert.That(scaled, Is.Not.Empty,
                    $"{protoId} must cook off through SolutionExplosionBehavior so an empty one is inert.");
                Assert.That(scaled.Select(b => b.Solution), Has.All.EqualTo(solution),
                    $"{protoId} must be scaled by its fuel solution, not some other one.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
