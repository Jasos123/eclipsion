using System.Collections.Generic;
using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Components;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Crescent;

/// <summary>
/// The bluespace corporate trader grids exist to be traded with, so their requisitions consoles have to resolve an
/// order database. A console only does that through the station owning its grid, and BluespaceErrorRule only makes
/// the grid a station when its stationMap names a game map whose station config is keyed to that map's own id.
/// </summary>
[TestFixture]
public sealed class BluespaceTraderTest
{
    /// <summary>Trader events, paired with the game map carrying the station config they spawn with.</summary>
    private static readonly object[] TraderEvents =
    {
        new object[] { "ShinoharaTrader", "KitamuraTrader" },
        new object[] { "PangTaiTrader", "GuTianTrader" }
    };

    /// <summary>Worldgen preset the traders have to stay clear of.</summary>
    private const string WorldgenConfig = "RatWorld";

    /// <summary>
    /// The traders are meant to be a run out of the belt, not something that materialises on top of it. A min/max
    /// box cannot express that - it contains the origin - so the events spawn on a ring instead, and that ring has
    /// to start outside everything worldgen scatters around the centre.
    /// </summary>
    [Test, TestCaseSource(nameof(TraderEvents))]
    public async Task TraderSpawnRingClearsTheBelt(string ruleId, string gameMapId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var compFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitPost(() =>
        {
            var rulePrototype = protoManager.Index<EntityPrototype>(ruleId);
            Assert.That(rulePrototype.TryGetComponent<BluespaceErrorRuleComponent>(out var rule, compFactory), Is.True,
                $"{ruleId} is not a BluespaceErrorRule.");

            Assert.That(rule!.MinDistance, Is.Not.Null,
                $"{ruleId} spawns from the min/max box, which contains the origin, so it can drop the trader on the belt.");
            Assert.That(rule.MaxDistance, Is.Not.Null, $"{ruleId} has a minDistance but no maxDistance.");
            Assert.That(rule.MinDistance!.Value, Is.LessThanOrEqualTo(rule.MaxDistance!.Value),
                $"{ruleId}'s spawn ring is inside out.");

            // Every biome worldgen scatters around the centre carves out a radius from the origin. The trader has
            // to start beyond the furthest of them.
            var config = protoManager.Index<WorldgenConfigPrototype>(WorldgenConfig);
            Assert.That(config.Components.TryGetComponent<BiomeSelectionComponent>(compFactory, out var selection), Is.True,
                $"{WorldgenConfig} selects no biomes.");

            var debrisEdge = 0f;
            var furthestBiome = string.Empty;
            foreach (var biomeId in selection!.Biomes)
            {
                if (protoManager.Index<BiomePrototype>(biomeId).DistanceRange is not { } range || range.Y <= debrisEdge)
                    continue;

                debrisEdge = range.Y;
                furthestBiome = biomeId;
            }

            Assert.That(debrisEdge, Is.GreaterThan(0f),
                $"{WorldgenConfig} has no distance-bounded biomes, so this test proves nothing - check the biome list.");
            Assert.That(rule.MinDistance.Value, Is.GreaterThan(debrisEdge),
                $"{ruleId} can spawn inside {furthestBiome}, which reaches out to {debrisEdge}.");
        });

        await pair.CleanReturnAsync();
    }

    [Test, TestCaseSource(nameof(TraderEvents))]
    public async Task TraderGridBecomesAStationWithAWorkingConsole(string ruleId, string gameMapId)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true // Stations spawn nullspace entities that outlive the map.
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var compFactory = server.ResolveDependency<IComponentFactory>();
        var mapManager = entManager.System<SharedMapSystem>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var stationSystem = entManager.System<StationSystem>();
        var linker = entManager.System<DeviceLinkSystem>();

        await server.WaitPost(() =>
        {
            Assert.That(protoManager.TryIndex<EntityPrototype>(ruleId, out var rulePrototype), Is.True,
                $"Trade event {ruleId} does not exist.");
            Assert.That(rulePrototype!.TryGetComponent<BluespaceErrorRuleComponent>(out var rule, compFactory), Is.True,
                $"{ruleId} is not a BluespaceErrorRule.");
            Assert.That(rule!.StationMap?.Id, Is.EqualTo(gameMapId),
                $"{ruleId} must name its station map, otherwise the grid never becomes a station and its console has no order database.");

            Assert.That(protoManager.TryIndex<GameMapPrototype>(gameMapId, out var proto), Is.True,
                $"{ruleId} points at missing game map {gameMapId}.");
            Assert.That(proto!.Stations.ContainsKey(gameMapId), Is.True,
                $"{gameMapId}'s station key must match the gameMap id, BluespaceErrorRule indexes Stations[gameMapID].");

            // Match the BluespaceErrorRule grid-loading path.
            mapManager.CreateMap(out var mapId);

            Assert.That(mapLoader.TryLoadGrid(mapId, new ResPath(rule.GridPath), out var loaded), Is.True,
                $"Failed to load {rule.GridPath} - was it saved as a map instead of a grid?");

            var gridUid = loaded!.Value.Owner;
            var station = stationSystem.InitializeNewStation(proto.Stations[gameMapId], new[] { gridUid });

            Assert.That(entManager.HasComponent<StationMemberComponent>(gridUid), Is.True,
                $"The {gameMapId} grid did not join a station.");
            Assert.That(entManager.HasComponent<StationCargoOrderDatabaseComponent>(station), Is.True,
                $"{gameMapId}'s station has no cargo order database, so orders are rejected as having no station.");

            var consoles = new List<EntityUid>();
            var consoleQuery = entManager.AllEntityQueryEnumerator<CargoOrderConsoleComponent, TransformComponent>();
            while (consoleQuery.MoveNext(out var uid, out var console, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                consoles.Add(uid);

                Assert.That(stationSystem.GetOwningStation(uid), Is.EqualTo(station),
                    $"A requisitions console on {gameMapId} resolves no station, so it opens on an empty balance.");

                var stock = protoManager.EnumeratePrototypes<CargoProductPrototype>()
                    .Count(product => console.AllowedGroups.Contains(product.Group));

                Assert.That(stock, Is.GreaterThan(0),
                    $"A requisitions console on {gameMapId} allows only groups no product uses, so its menu is empty.");
            }

            Assert.That(consoles, Is.Not.Empty, $"{gameMapId} has no requisitions console to trade through.");

            // An order only lands on a pad the ordering console is linked to, so both ends of the wiring matter.
            var pads = new List<EntityUid>();
            var padQuery = entManager.AllEntityQueryEnumerator<CargoTelepadComponent, TransformComponent>();
            while (padQuery.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                pads.Add(uid);

                Assert.That(consoles.Any(console => linker.GetLinks(console, uid).Count > 0), Is.True,
                    $"A cargo pad on {gameMapId} is linked to no requisitions console, so orders cannot land on it.");
            }

            foreach (var console in consoles)
            {
                Assert.That(pads.Any(pad => linker.GetLinks(console, pad).Count > 0), Is.True,
                    $"A requisitions console on {gameMapId} is linked to no cargo pad, so its orders are refused as unfulfillable.");
            }

            try
            {
                mapManager.DeleteMap(mapId);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete map {gameMapId}", ex);
            }
        });

        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }
}
