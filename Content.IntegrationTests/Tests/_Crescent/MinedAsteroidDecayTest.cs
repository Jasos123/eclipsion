#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Gatherable.Components;
using Content.Shared._Crescent.World;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class MinedAsteroidDecayTest
{
    private const string AsteroidProto = "RatAsteroidPoorLarge";

    // The sweep runs every five seconds, so a change needs a comfortable margin to be picked up regardless of the
    // tickrate the pool happens to run at.
    private const int SweepTicks = 400;

    [Test]
    public async Task AsteroidsDecayAfterHalfAnHour()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
        var compFactory = pair.Server.ResolveDependency<IComponentFactory>();

        var proto = protoMan.Index<EntityPrototype>(AsteroidProto);

        Assert.That(proto.TryGetComponent<MinedAsteroidDecayComponent>(out var decay, compFactory), Is.True,
            $"{AsteroidProto} needs the decay component or mined-out rocks pile up forever.");

        Assert.Multiple(() =>
        {
            Assert.That(decay!.DepletionThreshold, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(decay.DecayDelay, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(decay.ClaimWhitelist, Is.Not.Null,
                "Without a claim whitelist nothing on the rock can protect it.");
            Assert.That(decay.ClaimWhitelist!.Components,
                Does.Contain("FuelGenerator").And.Contains("PowerSupplier"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HollowedOutAsteroidIsDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = entMan.System<SharedMapSystem>();

        EntityUid roid = default;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            roid = entMan.SpawnEntity(AsteroidProto, new MapCoordinates(Vector2.Zero, mapId));
        });

        // Worldgen only fills the rock in once the locality loader reaches it.
        await pair.RunTicksSync(10);

        var initial = 0;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<MinedAsteroidDecayComponent>(roid);
            initial = comp.InitialRock;

            Assert.That(initial, Is.GreaterThan(0),
                "The baseline has to be taken once worldgen populates the rock, or nothing can ever decay.");
        });

        await server.WaitPost(() =>
        {
            MineOut(entMan, roid, 0.6f);

            // Skip the half-hour wait: the clock has already run out.
            var comp = entMan.GetComponent<MinedAsteroidDecayComponent>(roid);
            comp.CheckInterval = TimeSpan.Zero;
            comp.NextCheck = TimeSpan.Zero;
            comp.DecayAt = TimeSpan.Zero;
        });

        await pair.RunTicksSync(SweepTicks);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(roid), Is.True,
                $"A rock stripped of 60% of its {initial} ore and left unclaimed should have been cleaned up.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GearOnTheRockKeepsItAlive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = entMan.System<SharedMapSystem>();

        EntityUid roid = default;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            roid = entMan.SpawnEntity(AsteroidProto, new MapCoordinates(Vector2.Zero, mapId));
        });

        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            MineOut(entMan, roid, 0.6f);

            // A generator parked on the rock is somebody's claim on it.
            entMan.SpawnEntity("PortableGeneratorPacman", new EntityCoordinates(roid, Vector2.Zero));

            var comp = entMan.GetComponent<MinedAsteroidDecayComponent>(roid);
            comp.CheckInterval = TimeSpan.Zero;
            comp.NextCheck = TimeSpan.Zero;
            comp.DecayAt = TimeSpan.Zero;
        });

        await pair.RunTicksSync(SweepTicks);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(roid), Is.False,
                "An asteroid with a seep drill on it must never be cleaned up, however hollow it is.");
            Assert.That(entMan.GetComponent<MinedAsteroidDecayComponent>(roid).DecayAt, Is.Null,
                "A claimed asteroid should have its clock cleared, not just postponed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Destroys the given fraction of the rock standing on the grid, the way a crew with pickaxes would.
    /// </summary>
    private static void MineOut(IEntityManager entMan, EntityUid grid, float fraction)
    {
        var rock = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (entMan.HasComponent<GatherableComponent>(child))
                rock.Add(child);
        }

        var target = (int) MathF.Ceiling(rock.Count * fraction);
        for (var i = 0; i < target; i++)
        {
            entMan.DeleteEntity(rock[i]);
        }
    }
}
