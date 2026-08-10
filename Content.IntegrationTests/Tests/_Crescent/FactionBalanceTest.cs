using Content.Server._Crescent.Factions;
using Content.Server.GameTicking.Events;
using Content.Shared._Crescent.CCVar;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
[TestOf(typeof(FactionBalanceSystem))]
public sealed class FactionBalanceTest
{
    [Test]
    public async Task JobCheckRecountsPlayersFromTheCurrentTick()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
        });

        var server = pair.Server;
        var map = await pair.CreateTestMap();

        server.CfgMan.SetCVar(RatCCVars.FactionBalanceEnabled, true);
        server.CfgMan.SetCVar(RatCCVars.FactionBalanceAdminBypass, false);
        server.CfgMan.SetCVar(RatCCVars.FactionBalanceBaseSlots, 1);
        server.CfgMan.SetCVar(RatCCVars.FactionBalanceTolerance, 0);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var session = server.PlayerMan.Sessions.Single();
            var player = entMan.SpawnEntity(null, map.GridCoords);
            var membership = entMan.AddComponent<HullrotFactionComponent>(player);
            membership.Faction = "DSM";
            server.PlayerMan.SetAttachedEntity(session, player);

            var job = new ProtoId<JobPrototype>("UnionfallShipCrewDSM");
            var ev = new IsJobAllowedEvent(session, job);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);

            var balance = server.System<FactionBalanceSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(ev.Cancelled, Is.True);
                Assert.That(balance.IsJobBlocked(session, job, out var faction), Is.True);
                Assert.That(faction, Is.EqualTo("DSM"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
