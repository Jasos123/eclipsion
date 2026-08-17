using System.Threading;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Robust.Server.Player;
using Robust.Shared.Player;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.GameTicking.Rules;

public sealed class InactivityTimeRestartRuleSystem : GameRuleSystem<InactivityRuleComponent>
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(RunLevelChanged);
        _playerManager.PlayerStatusChanged += PlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= PlayerStatusChanged;
    }

    protected override void Ended(EntityUid uid, InactivityRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        StopTimer(uid, component);
    }

    public void RestartTimer(EntityUid uid, InactivityRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.TimerCancel.Cancel();
        component.TimerCancel = new CancellationTokenSource();
        Timer.Spawn(component.InactivityMaxTime, () => TimerFired(uid, component), component.TimerCancel.Token);
    }

    public void StopTimer(EntityUid uid, InactivityRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.TimerCancel.Cancel();
    }

    private void TimerFired(EntityUid uid, InactivityRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // Crescent - an empty server must not restart a mapping round; the mapper may just have stepped away.
        if (GameTicker.IsRoundEndBypassed())
        {
            Log.Info("Server sat empty, but a RoundEndBypass rule is active. Not ending the round.");
            return;
        }

        GameTicker.EndRound(Loc.GetString("rule-time-has-run-out"));

        _chatManager.DispatchServerAnnouncement(Loc.GetString("rule-restarting-in-seconds", ("seconds",(int) component.RoundEndDelay.TotalSeconds)));

        Timer.Spawn(component.RoundEndDelay, () => GameTicker.RestartRound());
    }

    private void RunLevelChanged(GameRunLevelChangedEvent args)
    {
        var query = EntityQueryEnumerator<InactivityRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var inactivity, out var gameRule))
        {
            // Crescent - continue, not return: one inactive rule used to abort the sweep and leave any active
            // rule behind it in the query with its timer neither started nor stopped.
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            switch (args.New)
            {
                case GameRunLevel.InRound:
                    RestartTimer(uid, inactivity);
                    break;
                case GameRunLevel.PreRoundLobby:
                case GameRunLevel.PostRound:
                    StopTimer(uid, inactivity);
                    break;
            }
        }
    }

    private void PlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        // Crescent - hoisted: neither of these depends on which rule we are looking at, and leaving the run level
        // check inside the loop made it read like it did.
        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;

        var serverEmpty = _playerManager.PlayerCount == 0;

        var query = EntityQueryEnumerator<InactivityRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var inactivity, out var gameRule))
        {
            // Crescent - continue, not return, for the same reason as RunLevelChanged: an inactive rule sitting
            // ahead of an active one in the query used to abort the sweep, so the active rule's timer was neither
            // started when the last player left nor stopped when somebody joined.
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            if (serverEmpty)
            {
                RestartTimer(uid, inactivity);
            }
            else
            {
                StopTimer(uid, inactivity);
            }
        }
    }
}
