using System.Collections.Generic;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
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
public sealed class GliessStationLoadTest
{
    private const string MapProto = "GliessSanto";

    /// <summary>Game rules that spawn Gliess Santo.</summary>
    private static readonly string[] RulesSpawningGliess =
    {
        "Adventure",
        "FreeplayDSMNCWL",
        "FreeplayTFSCSHI"
    };

    [Test]
    public async Task GliessLoadsAsAStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true // Stations spawn nullspace entities that outlive the map.
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapManager = entManager.System<SharedMapSystem>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var stationSystem = entManager.System<StationSystem>();

        await server.WaitPost(() =>
        {
            // Match the AdventureRule grid-loading path.
            var proto = protoManager.Index<GameMapPrototype>(MapProto);
            mapManager.CreateMap(out var mapId);

            Assert.That(mapLoader.TryLoadGrid(mapId, new ResPath(proto.MapPath.ToString()), out var loaded), Is.True,
                $"Failed to load {proto.MapPath} - was it saved as a map instead of a grid?");

            Assert.That(proto.Stations.ContainsKey(MapProto), Is.True,
                $"{MapProto}'s station key must match the gameMap id, AdventureRule indexes Stations[gameMapID].");

            var stationGrid = loaded!.Value.Owner;
            var station = stationSystem.InitializeNewStation(proto.Stations[MapProto], new[] { stationGrid });

            var gridUids = mapManager.GetAllGrids(mapId).Select(g => g.Owner).ToList();

            Assert.That(entManager.HasComponent<StationMemberComponent>(stationGrid), Is.True,
                $"The {MapProto} grid did not join a station - check the BecomesStation id.");

            Assert.That(entManager.HasComponent<PreventPilotComponent>(stationGrid), Is.True,
                $"{MapProto} is missing PreventPilot.");

            var jobsComp = entManager.GetComponent<StationJobsComponent>(station);
            var jobs = new HashSet<string>(jobsComp.SetupAvailableJobs.Keys);

            // Jobs must belong to a department to appear in selection menus.
            var departmentRoles = protoManager.EnumeratePrototypes<DepartmentPrototype>()
                .SelectMany(d => d.Roles)
                .ToHashSet();

            foreach (var jobId in jobs)
            {
                Assert.That(protoManager.HasIndex<JobPrototype>(jobId), Is.True,
                    $"{MapProto} offers job {jobId}, which has no prototype.");

                Assert.That(departmentRoles, Does.Contain(jobId),
                    $"{jobId} is in no department, so players cannot pick it in the lobby or latejoin menu.");
            }

            var hasLateJoin = false;
            var lateSpawns = entManager.AllEntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (lateSpawns.MoveNext(out var spawn, out var xform))
            {
                if (spawn.SpawnType is not SpawnPointType.LateJoin
                    || xform.GridUid == null
                    || !gridUids.Contains(xform.GridUid.Value))
                {
                    continue;
                }

                hasLateJoin = true;
                break;
            }

            Assert.That(hasLateJoin, Is.True, $"Found no latejoin spawn points on {MapProto}.");

            var jobSpawns = entManager.EntityQuery<SpawnPointComponent>()
                .Where(x => x.SpawnType == SpawnPointType.Job)
                .Select(x => x.Job!.ID);

            jobs.ExceptWith(jobSpawns);

            foreach (var jobId in jobs.ToList())
            {
                if (protoManager.TryIndex<JobPrototype>(jobId, out var jobProto) && jobProto.JobEntity != null)
                    jobs.Remove(jobId);
            }

            Assert.That(jobs, Is.Empty, $"There are no spawnpoints for {string.Join(", ", jobs)} on {MapProto}.");

            try
            {
                mapManager.DeleteMap(mapId);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete map {MapProto}", ex);
            }
        });

        await server.WaitRunTicks(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GamemodesReferencingGliessResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoManager = pair.Server.ResolveDependency<IPrototypeManager>();

        Assert.Multiple(() =>
        {
            Assert.That(protoManager.HasIndex<GameMapPrototype>(MapProto), Is.True);

            foreach (var ruleId in RulesSpawningGliess)
            {
                Assert.That(protoManager.HasIndex<EntityPrototype>(ruleId), Is.True,
                    $"Game rule {ruleId} does not exist.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
