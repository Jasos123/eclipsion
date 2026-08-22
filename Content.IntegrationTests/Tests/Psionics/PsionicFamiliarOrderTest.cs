using System.Numerics;
using Content.Server.Abilities.Psionics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Psionics;

/// <summary>
///     The Fire tree ships two actions whose only job is telling a summoned imp where to go and what
///     to hit, so a familiar that quietly ignores them takes a whole tier of the discipline with it.
/// </summary>
[TestFixture]
[TestOf(typeof(PsionicFamiliarSystem))]
public sealed class PsionicFamiliarOrderTest
{
    /// <summary>
    ///     A move order has to displace a standing attack order, and a recall has to clear both. The
    ///     two keys living side by side is what let the imp keep shooting through a move command.
    /// </summary>
    [Test]
    public async Task OrdersReplaceOneAnotherOnTheBlackboard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var master = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var psionic = entMan.EnsureComponent<PsionicComponent>(master);
            var familiar = entMan.SpawnEntity("MobPsionicFamiliarImp", map.GridCoords);
            var victim = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var familiarComp = entMan.EnsureComponent<PsionicFamiliarComponent>(familiar);
            familiarComp.Master = master;
            familiarComp.Commandable = true;
            psionic.Familiars.Add(familiar);

            var htn = entMan.GetComponent<HTNComponent>(familiar);

            var attack = new CommandPsionicFamiliarAttackActionEvent
            {
                Performer = master,
                Target = victim,
            };
            entMan.EventBus.RaiseLocalEvent(master, attack);

            Assert.Multiple(() =>
            {
                Assert.That(attack.Handled, Is.True, "The attack order was never handled.");
                Assert.That(
                    htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var ordered, entMan)
                    && ordered == victim,
                    "The attack order did not reach the familiar's blackboard.");
            });

            // Somewhere well clear of the master, so this reads as "go there" rather than "heel".
            var destination = new EntityCoordinates(map.Grid, new Vector2(8f, 8f));
            var move = new CommandPsionicFamiliarMoveActionEvent
            {
                Performer = master,
                Target = destination,
            };
            entMan.EventBus.RaiseLocalEvent(master, move);

            Assert.Multiple(() =>
            {
                Assert.That(move.Handled, Is.True, "The move order was never handled.");
                Assert.That(
                    htn.Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OrderedMoveTarget, out _, entMan),
                    "The move order did not reach the familiar's blackboard.");
                Assert.That(
                    htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out _, entMan),
                    Is.False,
                    "The familiar was still holding its attack order after being sent somewhere.");
                Assert.That(htn.Plan, Is.Null, "The running plan survived a new order.");
            });

            // Pointing at your own feet calls it back off the standing order.
            var recall = new CommandPsionicFamiliarMoveActionEvent
            {
                Performer = master,
                Target = entMan.GetComponent<TransformComponent>(master).Coordinates,
            };
            entMan.EventBus.RaiseLocalEvent(master, recall);

            Assert.Multiple(() =>
            {
                Assert.That(recall.Handled, Is.True, "The recall was never handled.");
                Assert.That(
                    htn.Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.OrderedMoveTarget, out _, entMan),
                    Is.False,
                    "The familiar was still holding position after being recalled.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     An order is an order: the familiar has to be able to answer for a target its own faction
    ///     is perfectly happy with, which the faction-filtered hostile query could never return.
    /// </summary>
    [Test]
    public async Task CommandedTargetQueryIgnoresFactions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var familiar = entMan.SpawnEntity("MobPsionicFamiliarImp", map.GridCoords);
            var friendly = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var htn = entMan.GetComponent<HTNComponent>(familiar);

            htn.Blackboard.SetValue(NPCBlackboard.CurrentOrderedTarget, friendly);

            var result = server.System<NPCUtilitySystem>().GetEntities(htn.Blackboard, "CommandedTarget");

            Assert.That(result.GetHighest(), Is.EqualTo(friendly));
        });

        await pair.CleanReturnAsync();
    }
}
