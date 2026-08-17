using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.GameTicking.Rules;

public sealed class MaxTimeRestartRuleSystem : GameRuleSystem<MaxTimeRestartRuleComponent>
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(RunLevelChanged);
    }

    protected override void Started(EntityUid uid, MaxTimeRestartRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if(GameTicker.RunLevel == GameRunLevel.InRound)
            RestartTimer(component);
    }

    protected override void Ended(EntityUid uid, MaxTimeRestartRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        StopTimer(component);
    }

    /// <summary>
    /// Crescent - arms the cap at a full <see cref="MaxTimeRestartRuleComponent.RoundMaxTime"/> from now, throwing
    /// away any admin adjustment. Also clears a pause, so re-arming a paused clock does not leave it stuck.
    /// </summary>
    public void RestartTimer(MaxTimeRestartRuleComponent component)
    {
        // TODO FULL GAME SAVE
        component.PausedRemaining = null;
        component.EndTime = Timing.CurTime + component.RoundMaxTime;
        component.LastCheck = Timing.CurTime;
    }

    public void StopTimer(MaxTimeRestartRuleComponent component)
    {
        component.EndTime = null;
        component.PausedRemaining = null;
    }

    /// <summary>
    /// Crescent - the cap runs off an absolute deadline checked here rather than a spawned timer, because admins
    /// need to read the time left, move it, and stop it. Warnings hang off the same deadline, so postponing the
    /// round end reschedules them with it instead of leaving stale announcements queued behind a dead clock.
    /// </summary>
    protected override void ActiveTick(EntityUid uid, MaxTimeRestartRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        // Disarmed: no round running, or an admin cancelled/paused the cap.
        if (component.EndTime is not { } endTime)
            return;

        // The round can be over well before the cap (conquest, admin restart). Nobody in a lobby needs a warning
        // about a clock that already stopped.
        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;

        var now = Timing.CurTime;

        // A mapping round is never going to hit the cap, so announcing it would just be a lie. Hold the window
        // open rather than stepping LastCheck past an instant whose announcement is suppressed: a bypass lifted
        // before the deadline used to have silently eaten whatever warning it sat through.
        if (!GameTicker.IsRoundEndBypassed())
        {
            var last = component.LastCheck;
            component.LastCheck = now;

            // Normally the one instant this tick crossed, but a window held open by a bypass - or by a server
            // hitch - can hold several. Only the nearest one is still true, so the rest are dropped instead of
            // going out as a burst that counts the round down twice.
            RoundTimeWarning? due = null;
            foreach (var warning in component.Warnings)
            {
                var at = endTime - warning.Before;

                // A warning scheduled further out than the round is long sits before the round even started, so
                // it is already behind `last` and never fires.
                if (at > last && at <= now && (due == null || warning.Before < due.Before))
                    due = warning;
            }

            if (due != null)
                WarningFired(due);
        }

        if (now < endTime)
            return;

        // A mapping round must not be restarted out from under the mapper, but the cap must not be thrown away
        // on its account either. Leaving the deadline armed holds it exactly where it is, so the round ends the
        // moment the bypass is lifted - disarming here instead meant the cap silently vanished for the rest of
        // the round and `roundtimer status` reported CANCELLED with nobody having cancelled anything.
        if (GameTicker.IsRoundEndBypassed())
            return;

        // Disarm before firing so an already-ending round does not re-enter this every tick.
        component.EndTime = null;
        TimerFired(component);
    }

    /// <summary>
    /// Crescent - the in-character heads-up that the cap is coming. Deliberately goes out as a radio announcement
    /// rather than a server message: it is diegetic, and the crew is meant to act on it.
    /// </summary>
    private void WarningFired(RoundTimeWarning warning)
    {
        // The bypass check lives in ActiveTick, where it can hold the warning window open instead of letting a
        // suppressed announcement consume its instant.
        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString(warning.Message, ("minutes", (int) warning.Before.TotalMinutes)),
            warning.Sender is { } sender ? Loc.GetString(sender) : "Central Command",
            announcementSound: warning.Sound,
            colorOverride: warning.Color);
    }

    private void TimerFired(MaxTimeRestartRuleComponent component)
    {
        // Crescent - a mapping round hitting the time cap must not restart the server out from under the mapper.
        if (GameTicker.IsRoundEndBypassed())
        {
            Log.Info("Max round time elapsed, but a RoundEndBypass rule is active. Not ending the round.");
            return;
        }

        GameTicker.EndRound(Loc.GetString("rule-time-has-run-out"));

        _chatManager.DispatchServerAnnouncement(Loc.GetString("rule-restarting-in-seconds",("seconds", (int) component.RoundEndDelay.TotalSeconds)));

        // TODO FULL GAME SAVE
        Timer.Spawn(component.RoundEndDelay, () => GameTicker.RestartRound());
    }

    private void RunLevelChanged(GameRunLevelChangedEvent args)
    {
        var query = EntityQueryEnumerator<MaxTimeRestartRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var timer, out var gameRule))
        {
            // Crescent - continue, not return. A single inactive rule used to abort the whole sweep, so any
            // active MaxTimeRestartRule sitting behind it in the query never got its timer started or stopped:
            // the round cap silently vanished, and a timer left running into post-round could still fire.
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            switch (args.New)
            {
                case GameRunLevel.InRound:
                    RestartTimer(timer);
                    break;
                case GameRunLevel.PreRoundLobby:
                case GameRunLevel.PostRound:
                    StopTimer(timer);
                    break;
            }
        }
    }

    #region Admin control

    // Crescent - the `roundtimer` admin command drives the cap through here. Anything an admin can do to the clock
    // is a change to EndTime/PausedRemaining and nothing else, so the rule cannot end up half-armed.

    /// <summary>
    /// Crescent - every round-length cap that is currently running. Normally exactly one (the preset's), but a
    /// hand-added rule can stack, and admins have to be told when that happens rather than silently hitting one.
    /// </summary>
    public List<Entity<MaxTimeRestartRuleComponent>> GetActiveCaps()
    {
        var caps = new List<Entity<MaxTimeRestartRuleComponent>>();

        var query = EntityQueryEnumerator<MaxTimeRestartRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var cap, out var gameRule))
        {
            if (GameTicker.IsGameRuleActive(uid, gameRule))
                caps.Add((uid, cap));
        }

        return caps;
    }

    /// <summary>
    /// Crescent - time left before the round ends, or null if the cap is disarmed (cancelled). A paused cap
    /// reports the frozen remainder, since that is what resuming restores.
    /// </summary>
    public TimeSpan? GetRemaining(MaxTimeRestartRuleComponent component)
    {
        if (component.PausedRemaining is { } paused)
            return paused;

        if (component.EndTime is not { } endTime)
            return null;

        var remaining = endTime - Timing.CurTime;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool IsPaused(MaxTimeRestartRuleComponent component) => component.PausedRemaining != null;

    /// <summary>
    /// Crescent - stops the round from ending on the clock at all. The rule stays running so an admin can put a
    /// deadline back with `roundtimer set`/`reset`; nothing else about the round changes.
    /// </summary>
    public void CancelCap(MaxTimeRestartRuleComponent component)
    {
        component.EndTime = null;
        component.PausedRemaining = null;
    }

    /// <summary>
    /// Crescent - pushes the deadline out (or pulls it in, on a negative delta) without disturbing the pause
    /// state. Returns the new remaining time, or null if there was no deadline to move.
    /// </summary>
    public TimeSpan? AddTime(MaxTimeRestartRuleComponent component, TimeSpan delta)
    {
        if (component.PausedRemaining is { } paused)
        {
            component.PausedRemaining = Max(paused + delta, TimeSpan.Zero);
            return component.PausedRemaining;
        }

        if (component.EndTime is not { } endTime)
            return null;

        // Clamp to now rather than the past: a shortening overshoot should end the round on the next tick, not
        // land behind LastCheck where the warnings can never be crossed again.
        component.EndTime = Max(endTime + delta, Timing.CurTime);
        return GetRemaining(component);
    }

    /// <summary>
    /// Crescent - sets the time left outright, re-arming a cancelled cap. A paused cap stays paused and resumes
    /// on the new number.
    /// </summary>
    public void SetRemaining(MaxTimeRestartRuleComponent component, TimeSpan remaining)
    {
        remaining = Max(remaining, TimeSpan.Zero);

        if (component.PausedRemaining != null)
        {
            component.PausedRemaining = remaining;
            return;
        }

        component.EndTime = Timing.CurTime + remaining;
        component.LastCheck = Timing.CurTime;
    }

    /// <summary>
    /// Crescent - freezes the countdown where it is. Returns false if there was no running clock to freeze.
    /// </summary>
    public bool PauseCap(MaxTimeRestartRuleComponent component)
    {
        if (component.PausedRemaining != null || component.EndTime is not { } endTime)
            return false;

        component.PausedRemaining = Max(endTime - Timing.CurTime, TimeSpan.Zero);
        component.EndTime = null;
        return true;
    }

    /// <summary>
    /// Crescent - restarts a frozen countdown from the remainder it kept. Returns false if it was not paused.
    /// </summary>
    public bool ResumeCap(MaxTimeRestartRuleComponent component)
    {
        if (component.PausedRemaining is not { } paused)
            return false;

        component.PausedRemaining = null;
        component.EndTime = Timing.CurTime + paused;
        component.LastCheck = Timing.CurTime;
        return true;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    #endregion
}
