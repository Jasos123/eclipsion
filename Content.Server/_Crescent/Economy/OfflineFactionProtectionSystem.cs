using Content.Server.GameTicking.Events;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Economy;

/// <summary>
/// Tracks whether a faction has fielded enough real players this round to expose its persistent
/// economy. A faction remains protected until two distinct players have represented it, and becomes
/// protected again when none of its players have been active for an hour.
/// </summary>
public sealed class OfflineFactionProtectionSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public const int MinimumRoundPlayers = 2;
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromHours(1);

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, FactionActivity> _activity =
        new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetRound());
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);

        _nextScan = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextScan)
            RefreshActivity();
    }

    private void ResetRound()
    {
        _activity.Clear();
        _nextScan = _timing.CurTime;
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (!TryComp<HullrotFactionComponent>(args.Mob, out var member))
            return;

        RecordActivity(member.Faction, args.Player.UserId);
    }

    /// <summary>
    /// Records every connected player currently occupying a faction body. The user id set is
    /// retained for the round, while the timestamp advances only while somebody is actually present.
    /// </summary>
    public void RefreshActivity()
    {
        var now = _timing.CurTime;
        _nextScan = now + ScanInterval;

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { Valid: true } attached)
                continue;

            if (!TryComp<HullrotFactionComponent>(attached, out var member))
                continue;

            RecordActivity(member.Faction, session.UserId);
        }
    }

    private void RecordActivity(string faction, NetUserId player)
    {
        if (string.IsNullOrWhiteSpace(faction))
            return;

        if (!_activity.TryGetValue(faction, out var state))
        {
            state = new FactionActivity();
            _activity[faction] = state;
        }

        state.RoundPlayers.Add(player);
        state.LastActive = _timing.CurTime;
    }

    /// <summary>
    /// Returns whether the faction's stock and hostile treasury interaction must be blocked.
    /// </summary>
    public bool IsProtected(string faction)
    {
        // An unowned or incorrectly configured console has no faction whose activity can be measured.
        if (string.IsNullOrWhiteSpace(faction))
            return false;

        // Callers such as a treasury click may arrive before the periodic update for this frame.
        if (_timing.CurTime >= _nextScan)
            RefreshActivity();

        return !_activity.TryGetValue(faction, out var state) ||
               ShouldProtect(state.RoundPlayers.Count, state.LastActive, _timing.CurTime);
    }

    /// <summary>Pure policy helper, kept public so the boundary conditions can be tested directly.</summary>
    public static bool ShouldProtect(int roundPlayers, TimeSpan? lastActive, TimeSpan now)
    {
        return roundPlayers < MinimumRoundPlayers ||
               lastActive is null ||
               now - lastActive.Value > ActivityWindow;
    }

    private sealed class FactionActivity
    {
        public readonly HashSet<NetUserId> RoundPlayers = new();
        public TimeSpan? LastActive;
    }
}
