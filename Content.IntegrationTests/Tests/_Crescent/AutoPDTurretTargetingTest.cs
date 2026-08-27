using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
[TestOf(typeof(NPCUtilitySystem))]
public sealed class AutoPDTurretTargetingTest
{
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
