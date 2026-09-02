using System.Numerics;
using Content.Server._Crescent.Factions;
using Content.Server._Crescent.Territory;
using Content.Shared._Crescent.Factions;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._Crescent.Territory;
using Content.Shared.CaptureFlag;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class PersistentCaptureRegionTest
{
    [Test]
    public async Task DsmAndNcwlCanCaptureAndReassignAntiBoarderTurret()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid flagUid = default;
        EntityUid turretUid = default;
        EntityUid playerUid = default;
        EntityUid lateTurretUid = default;

        await server.WaitAssertion(() =>
        {
            flagUid = entMan.CreateEntityUninitialized("PersistentCaptureRegionFlag", map.GridCoords);
            var region = entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid);
            region.RegionId = $"integration-test-{Guid.NewGuid():N}";
            entMan.InitializeAndStartEntity(flagUid);

            turretUid = entMan.SpawnEntity("WeaponTurretAutoPDCapturable", map.GridCoords);
            playerUid = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var flag = entMan.GetComponent<CaptureFlagComponent>(flagUid);
            flag.CaptureTime = 0.05f;
            flag.NeutralizeTime = 0.05f;

            var faction = entMan.EnsureComponent<HullrotFactionComponent>(playerUid);
            faction.Faction = "DSM";
            server.System<HullrotNpcFactionSyncSystem>().Sync(playerUid, faction);
            server.System<MobStateSystem>().ChangeMobState(playerUid, MobState.Dead);
        });

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<CaptureFlagComponent>(flagUid).OwnerTeam, Is.Null,
                "A dead faction member captured persistent territory.");
            Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(turretUid).Factions, Is.Empty);
            server.System<MobStateSystem>().ChangeMobState(playerUid, MobState.Alive);
        });

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<CaptureFlagComponent>(flagUid).OwnerTeam, Is.EqualTo("DSM"));
            Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(turretUid).Factions, Does.Contain("DSM"));
        });

        await server.WaitAssertion(() =>
        {
            var faction = entMan.GetComponent<HullrotFactionComponent>(playerUid);
            faction.Faction = "NCWL";
            server.System<HullrotNpcFactionSyncSystem>().Sync(playerUid, faction);
        });

        await server.WaitRunTicks(20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<CaptureFlagComponent>(flagUid).OwnerTeam, Is.EqualTo("NCWL"));
            var turretFactions = entMan.GetComponent<NpcFactionMemberComponent>(turretUid).Factions;
            Assert.That(turretFactions, Does.Contain("NCWL"));
            Assert.That(turretFactions, Does.Not.Contain("DSM"));

            lateTurretUid = entMan.SpawnEntity("WeaponTurretAutoPDCapturable", map.GridCoords);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(lateTurretUid).Factions,
                Does.Contain("NCWL"), "A device spawned after capture did not inherit the current owner.");

            var otherGrid = server.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            var xform = entMan.GetComponent<TransformComponent>(lateTurretUid);
            var transform = server.System<SharedTransformSystem>();
            transform.Unanchor(lateTurretUid, xform);
            transform.SetCoordinates(lateTurretUid, xform, new EntityCoordinates(otherGrid, Vector2.Zero));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(lateTurretUid).Factions, Is.Empty,
                "A device carried stale territory ownership onto another grid.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PartialProgressCannotBeInheritedByAnotherFaction()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid flagUid = default;
        EntityUid playerUid = default;
        var dsmProgress = 0f;

        await server.WaitAssertion(() =>
        {
            flagUid = entMan.CreateEntityUninitialized("PersistentCaptureRegionFlag", map.GridCoords);
            var region = entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid);
            region.RegionId = $"integration-test-{Guid.NewGuid():N}";
            entMan.InitializeAndStartEntity(flagUid);

            var flag = entMan.GetComponent<CaptureFlagComponent>(flagUid);
            flag.CaptureTime = 1f;
            flag.NeutralizeTime = 1f;

            playerUid = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var faction = entMan.EnsureComponent<HullrotFactionComponent>(playerUid);
            faction.Faction = "DSM";
            server.System<HullrotNpcFactionSyncSystem>().Sync(playerUid, faction);
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var flag = entMan.GetComponent<CaptureFlagComponent>(flagUid);
            dsmProgress = flag.ProgressSeconds;
            Assert.That(dsmProgress, Is.GreaterThan(0f).And.LessThan(flag.CaptureTime));

            var faction = entMan.GetComponent<HullrotFactionComponent>(playerUid);
            faction.Faction = "NCWL";
            server.System<HullrotNpcFactionSyncSystem>().Sync(playerUid, faction);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            var flag = entMan.GetComponent<CaptureFlagComponent>(flagUid);
            Assert.That(flag.ProgressTeam, Is.EqualTo("NCWL"));
            Assert.That(flag.ProgressSeconds, Is.LessThan(dsmProgress),
                "NCWL inherited DSM's partial capture progress.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The four supported powers are not a cosmetic list. A faction that cannot hold territory has to fail here
    /// rather than at runtime, where an unknown NPC faction leaves a captured turret shooting nobody and a
    /// missing banner state leaves the flag stuck on its neutral colours.
    /// </summary>
    [Test]
    public async Task EverySupportedFactionCanActuallyHoldTerritory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
        var compFactory = pair.Server.ResolveDependency<IComponentFactory>();

        var flagProto = protoMan.Index<EntityPrototype>("PersistentCaptureRegionFlag");

        Assert.Multiple(() =>
        {
            Assert.That(flagProto.TryGetComponent<PersistentCaptureRegionComponent>(out var region, compFactory),
                Is.True, "The territory flag must carry a PersistentCaptureRegion.");
            Assert.That(flagProto.TryGetComponent<CaptureFlagComponent>(out var flag, compFactory), Is.True);

            // A freeplay territory must never be able to end the round the way the capture gamemode's flags do.
            Assert.That(flag!.DominationEnabled, Is.False,
                "A persistent territory flag must not count toward a domination win.");

            foreach (var faction in PersistentTerritoryFactions.Supported)
            {
                Assert.That(protoMan.HasIndex<NpcFactionPrototype>(faction), Is.True,
                    $"{faction} has no npcFaction prototype, so a captured turret could not join it.");

                Assert.That(region!.TeamStates.ContainsKey(faction), Is.True,
                    $"{faction} has no banner sprite state on the territory flag.");

                Assert.That(
                    region.FactionColors.ContainsKey(faction) || protoMan.HasIndex<FactionPrototype>(faction),
                    Is.True,
                    $"{faction} has neither a radar colour nor a faction prototype to take one from.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Ownership outlives the round, so an admin has to be able to move it without editing the save by hand.
    /// The override has to reach everything a player capture would, and releasing it has to give all of it back.
    /// </summary>
    [Test]
    public async Task AdminOverrideTakesAndReleasesTerritory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid flagUid = default;
        EntityUid turretUid = default;
        var regionId = $"integration-test-{Guid.NewGuid():N}";
        string turretBaseName = string.Empty;

        await server.WaitAssertion(() =>
        {
            flagUid = entMan.CreateEntityUninitialized("PersistentCaptureRegionFlag", map.GridCoords);
            entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid).RegionId = regionId;
            entMan.InitializeAndStartEntity(flagUid);

            turretUid = entMan.SpawnEntity("WeaponTurretAutoPDCapturable", map.GridCoords);
            turretBaseName = entMan.GetComponent<MetaDataComponent>(turretUid).EntityName;
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var territory = server.System<PersistentCaptureRegionSystem>();

            Assert.That(territory.SetOwner(regionId, "NotAFaction"), Is.False,
                "A faction that cannot hold territory was accepted.");
            Assert.That(territory.SetOwner(regionId, "SHI"), Is.True);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var territory = server.System<PersistentCaptureRegionSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<CaptureFlagComponent>(flagUid).OwnerTeam, Is.EqualTo("SHI"));
                Assert.That(entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid).AppliedOwner,
                    Is.EqualTo("SHI"));
                Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(turretUid).Factions, Does.Contain("SHI"));
                Assert.That(entMan.GetComponent<MetaDataComponent>(turretUid).EntityName,
                    Is.EqualTo($"SHI {turretBaseName}"),
                    "A captured turret should read as the holder's.");

                var row = territory.GetRegions().FirstOrDefault(r => r.RegionId == regionId);
                Assert.That(row.RegionId, Is.EqualTo(regionId), "The region is missing from the admin listing.");
                Assert.That(row.Owner, Is.EqualTo("SHI"));
                Assert.That(row.Loaded, Is.True);
            });

            territory.SetOwner(regionId, null);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var territory = server.System<PersistentCaptureRegionSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<CaptureFlagComponent>(flagUid).OwnerTeam, Is.Null);
                Assert.That(entMan.GetComponent<PersistentCaptureRegionComponent>(flagUid).AppliedOwner, Is.Null);
                Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(turretUid).Factions, Is.Empty);
                Assert.That(entMan.GetComponent<MetaDataComponent>(turretUid).EntityName, Is.EqualTo(turretBaseName),
                    "A released turret kept the old holder's name.");
            });

            // Leave nothing of this test behind in the persistence file.
            Assert.That(territory.ForgetRegion(regionId), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
