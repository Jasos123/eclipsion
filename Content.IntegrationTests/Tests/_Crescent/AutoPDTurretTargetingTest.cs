using System.Numerics;
using Content.Server.Mech.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._Crescent.Diplomacy;
using Content.Server._Crescent.Factions;
using Content.Shared._Crescent.Diplomacy;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.Inventory;
using Content.Shared.Mech.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
[TestOf(typeof(NPCUtilitySystem))]
public sealed class AutoPDTurretTargetingTest
{
    [TestCase(MobState.Critical)]
    [TestCase(MobState.SoftCritical)]
    [TestCase(MobState.Dead)]
    public async Task IncapacitatedPilotIsNotTargeted(MobState state)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var turret = entMan.SpawnEntity("WeaponTurretAutoPDDSM", map.GridCoords);
            var mech = entMan.SpawnEntity("MechRipley", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var pilot = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));

            Assert.That(server.System<MechSystem>().TryInsert(mech, pilot, entMan.GetComponent<MechComponent>(mech)), Is.True);
            server.System<MobStateSystem>().ChangeMobState(pilot, state);

            var htn = entMan.GetComponent<HTNComponent>(turret);
            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "NearbyPDTTargets");

            Assert.That(result.GetHighest(), Is.EqualTo(EntityUid.Invalid),
                $"The anti-boarder turret targeted a pilot in the {state} state.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WornAlliedFactionIdPreventsTargeting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var turret = entMan.SpawnEntity("WeaponTurretAutoPDDSM", map.GridCoords);
            var mech = entMan.SpawnEntity("MechRipley", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var ally = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var id = entMan.SpawnEntity("SHIIDCardEmployee", MapCoordinates.Nullspace);

            Assert.That(server.System<InventorySystem>().TryEquip(ally, id, "id", silent: true, force: true), Is.True);
            Assert.That(server.System<MechSystem>().TryInsert(mech, ally, entMan.GetComponent<MechComponent>(mech)), Is.True);

            var factionId = entMan.GetComponent<FactionIdCardComponent>(id);
            Assert.That(factionId.Faction, Is.EqualTo("SHI"),
                "The preset SHI ID did not learn its faction from its job.");

            var diplomacy = server.System<RatDiplomacySystem>();
            var previousRelation = diplomacy.GetRelation("DSM", "SHI");
            diplomacy.SetRelation("DSM", "SHI", FactionRelation.Alliance, persist: false);

            var htn = entMan.GetComponent<HTNComponent>(turret);
            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "NearbyPDTTargets");
            diplomacy.SetRelation("DSM", "SHI", previousRelation, persist: false);

            Assert.That(result.GetHighest(), Is.EqualTo(EntityUid.Invalid),
                "The anti-boarder turret targeted someone wearing an allied faction ID.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WornHostileFactionIdTargetsHardsuitlessWearer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var turret = entMan.SpawnEntity("WeaponTurretAutoPDDSM", map.GridCoords);
            var boarder = entMan.SpawnEntity("MobHuman",
                new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var id = entMan.SpawnEntity("NCWLIDCardWorker", MapCoordinates.Nullspace);

            Assert.That(server.System<InventorySystem>().TryEquip(boarder, id, "id", silent: true, force: true),
                Is.True);
            Assert.That(entMan.GetComponent<FactionIdCardComponent>(id).Faction, Is.EqualTo("NCWL"));
            Assert.That(server.System<RatDiplomacySystem>().GetRelation("DSM", "NCWL"),
                Is.EqualTo(FactionRelation.War));

            var htn = entMan.GetComponent<HTNComponent>(turret);
            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "NearbyPDTTargets");

            Assert.That(result.GetHighest(), Is.EqualTo(boarder),
                "The anti-boarder turret ignored a hardsuitless wearer of a hostile faction ID.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrdinaryMechTargetsPilot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var turret = entMan.SpawnEntity("WeaponTurretAutoPDDSM", map.GridCoords);
            var mech = entMan.SpawnEntity("MechRipley", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var pilot = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var mechComp = entMan.GetComponent<MechComponent>(mech);

            Assert.That(server.System<MechSystem>().TryInsert(mech, pilot, mechComp), Is.True);

            var htn = entMan.GetComponent<HTNComponent>(turret);
            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "NearbyPDTTargets");

            Assert.That(result.GetHighest(), Is.EqualTo(pilot),
                "The anti-boarder turret targeted an ordinary mech instead of its pilot.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionMechTargetsMech()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var turret = entMan.SpawnEntity("WeaponTurretAutoPDDSM", map.GridCoords);
            var mech = entMan.SpawnEntity("MechNCWLBogatyr", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var pilot = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            var mechComp = entMan.GetComponent<MechComponent>(mech);

            Assert.That(server.System<MechSystem>().TryInsert(mech, pilot, mechComp), Is.True);

            var htn = entMan.GetComponent<HTNComponent>(turret);
            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "NearbyPDTTargets");

            Assert.That(result.GetHighest(), Is.EqualTo(mech),
                "The anti-boarder turret targeted a faction mech's pilot instead of the mech.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DismantlingDoesNotTargetSameHullrotFaction()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var turret = entMan.SpawnEntity("WeaponTurretAutoPDDSM", map.GridCoords);
            var dsmCrew = entMan.SpawnEntity(null,
                new EntityCoordinates(map.Grid, new Vector2(1f, 0f)));
            entMan.AddComponent<MobStateComponent>(dsmCrew);
            entMan.AddComponent(dsmCrew, new HullrotFactionComponent { Faction = "DSM" });

            var faction = server.System<NpcFactionSystem>();
            // Model a missing/stale NPC-faction mirror. HullrotFaction remains the authoritative allegiance
            // used by jobs and recruitment, so the anti-boarder turret must still recognize its own crew.
            faction.RemoveFaction(dsmCrew, "DSM");

            var xform = entMan.GetComponent<TransformComponent>(turret);
            server.System<SharedTransformSystem>().Unanchor(turret, xform);

            var htn = entMan.GetComponent<HTNComponent>(turret);
            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "NearbyPDTTargets");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(faction.IsMember(turret, "DSM"), Is.True,
                    "The DSM anti-boarder turret lost its faction.");
                Assert.That(result.GetHighest(), Is.EqualTo(EntityUid.Invalid),
                    "Unanchoring the turret made DSM crew a valid target.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
