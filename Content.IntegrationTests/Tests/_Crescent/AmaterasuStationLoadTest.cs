using System.Collections.Generic;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class AmaterasuStationLoadTest
{
    private const string MapProto = "Amaterasu";

    [Test]
    public async Task AmaterasuLoadsAsMobileShinoharaBase()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var stationSystem = entManager.System<StationSystem>();

        await server.WaitPost(() =>
        {
            var proto = protoManager.Index<GameMapPrototype>(MapProto);
            mapSystem.CreateMap(out var mapId);

            Assert.That(mapLoader.TryLoadGrid(mapId, proto.MapPath, out var loaded), Is.True,
                $"Failed to load {proto.MapPath} as a grid.");
            Assert.That(proto.Stations.ContainsKey(MapProto), Is.True,
                $"{MapProto}'s station key must match the gameMap id.");

            var stationGrid = loaded!.Value.Owner;
            var station = stationSystem.InitializeNewStation(proto.Stations[MapProto], new[] { stationGrid });

            Assert.Multiple(() =>
            {
                Assert.That(entManager.HasComponent<StationMemberComponent>(stationGrid), Is.True,
                    "Amaterasu did not join its station.");
                Assert.That(entManager.HasComponent<ShuttleComponent>(stationGrid), Is.True,
                    "Amaterasu must remain pilotable as a mobile base.");
                Assert.That(entManager.HasComponent<PreventPilotComponent>(stationGrid), Is.False,
                    "Amaterasu must not be marked as unpiloted.");
            });

            var jobs = entManager.GetComponent<StationJobsComponent>(station).SetupAvailableJobs.Keys.ToHashSet();
            Assert.That(jobs, Is.EquivalentTo(new HashSet<ProtoId<JobPrototype>>
            {
                "ExecutiveSHI",
                "BoardSHI",
                "EmployeeSHI",
            }));

            mapSystem.DeleteMap(mapId);
        });

        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }
}
