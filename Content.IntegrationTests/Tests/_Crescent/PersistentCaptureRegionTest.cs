using Content.Server._Crescent.Factions;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._Crescent.Territory;
using Content.Shared.CaptureFlag;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;

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
        });

        await pair.CleanReturnAsync();
    }
}
