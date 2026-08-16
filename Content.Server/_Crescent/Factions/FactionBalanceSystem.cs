using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared._Crescent.CCVar;
using Content.Shared._Crescent.Factions.FactionBalance;
using Content.Shared._Crescent.HullrotFaction;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Factions;

/// <summary>
/// Population-scaled spawn tickets. Each faction may only hold its share of the players currently in the
/// round, so a faction that is already ahead stops accepting new arrivals until the others catch up.
/// Counts are live: a player who ghosts, dies out of their body or disconnects gives their slot back, so
/// the side taking losses is always the side that can be reinforced.
/// </summary>
public sealed class FactionBalanceSystem : SharedFactionBalanceSystem
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    /// <summary>
    /// How often the headcounts are recounted. Joining, ghosting and disconnecting all move the numbers,
    /// so a cheap poll is steadier than chasing every event that could matter.
    /// </summary>
    private static readonly TimeSpan RecountInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextRecount;

    private Dictionary<string, FactionBalanceEntry> _state = new();

    /// <summary>
    /// Factions this round can be joined as, cached because the answer only changes when a station is set
    /// up. Null means it has to be worked out again.
    /// </summary>
    private HashSet<string>? _factionsInPlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<IsJobAllowedEvent>(OnIsJobAllowed);
        SubscribeLocalEvent<GetDisallowedJobsEvent>(OnGetDisallowedJobs);

        Subs.CVar(_cfg, RatCCVars.FactionBalanceEnabled, OnStateConfigurationChanged, false);
        Subs.CVar(_cfg, RatCCVars.FactionBalanceAdminBypass, OnStateConfigurationChanged, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRecount)
            return;

        _nextRecount = _timing.CurTime + RecountInterval;
        Recount();
    }

    #region Enforcement

    private void OnIsJobAllowed(ref IsJobAllowedEvent ev)
    {
        // Round-start players are spawned sequentially in one tick. Refresh synchronously so each
        // decision sees the bodies created earlier in the same loop instead of the last one-second poll.
        Recount();

        if (IsJobBlocked(ev.Player, ev.JobId, out var faction))
        {
            ev.Cancelled = true;
            _chat.DispatchServerMessage(ev.Player, GetRefusalMessage(faction));
        }
    }

    private void OnGetDisallowedJobs(ref GetDisallowedJobsEvent ev)
    {
        if (!_cfg.GetCVar(RatCCVars.FactionBalanceEnabled) || IsExempt(ev.Player))
            return;

        foreach (var (faction, entry) in _state)
        {
            if (!entry.Full)
                continue;

            foreach (var jobId in GetFactionJobs(faction))
            {
                ev.Jobs.Add(jobId);
            }
        }
    }

    /// <summary>
    /// Whether this player would be refused the given job right now, and which faction refused them.
    /// </summary>
    public bool IsJobBlocked(ICommonSession player, string jobId, out string faction)
    {
        faction = string.Empty;

        if (!_cfg.GetCVar(RatCCVars.FactionBalanceEnabled) || IsExempt(player))
            return false;

        if (!TryGetJobFaction(jobId, out faction))
            return false;

        return _state.TryGetValue(faction, out var entry) && entry.Full;
    }

    private bool IsExempt(ICommonSession player)
    {
        return _cfg.GetCVar(RatCCVars.FactionBalanceAdminBypass) && _admin.IsAdmin(player);
    }

    private string GetRefusalMessage(string faction)
    {
        var name = _prototype.TryIndex<FactionPrototype>(faction, out var proto) ? proto.Name : faction;
        var entry = _state.TryGetValue(faction, out var e) ? e : default;

        return Loc.GetString("faction-balance-join-refused",
            ("faction", name),
            ("count", entry.Count),
            ("cap", entry.Cap));
    }

    #endregion

    #region Counting

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _state.Clear();
        _factionsInPlay = null;
        _nextRecount = TimeSpan.Zero;
    }

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        // The new station's jobs may belong to a faction that was not in the round a moment ago.
        _factionsInPlay = null;
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        RaiseNetworkEvent(BuildStateEvent(), ev.PlayerSession.Channel);
    }

    private void OnStateConfigurationChanged(bool _)
    {
        // These flags do not affect the calculated counts or caps, so Recount's equality check
        // cannot notice them. Push a fresh snapshot explicitly for open late-join windows.
        RaiseNetworkEvent(BuildStateEvent());
    }

    private void Recount()
    {
        var counts = new Dictionary<string, int>();

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } attached)
                continue;

            // Ghosts and observers never carry the component, so they drop out of the count on their own.
            if (!TryComp<HullrotFactionComponent>(attached, out var faction) || faction.Faction == string.Empty)
                continue;

            counts[faction.Faction] = counts.GetValueOrDefault(faction.Faction) + 1;
        }

        _factionsInPlay ??= GetFactionsInPlay();

        var updated = CalculateCaps(counts,
            _cfg.GetCVar(RatCCVars.FactionBalanceBaseSlots),
            _cfg.GetCVar(RatCCVars.FactionBalanceTolerance),
            _factionsInPlay);

        if (Matches(updated, _state))
            return;

        _state = updated;
        RaiseNetworkEvent(BuildStateEvent());
    }

    /// <summary>
    /// Which factions the round was set up to be played as, read from the job lists the stations carry.
    /// A gamemode that leaves a faction off the map list leaves it out of the balance entirely, so the
    /// sides that are being played are never measured against one that cannot arrive. Depleted jobs still
    /// count: a faction whose slots are all taken is in the round, it is just full.
    /// </summary>
    private HashSet<string> GetFactionsInPlay()
    {
        var inPlay = new HashSet<string>();
        var query = EntityQueryEnumerator<StationJobsComponent>();

        while (query.MoveNext(out var stationJobs))
        {
            foreach (var jobId in stationJobs.JobList.Keys)
            {
                if (TryGetJobFaction(jobId, out var faction))
                    inPlay.Add(faction);
            }
        }

        return inPlay;
    }

    private FactionBalanceStateEvent BuildStateEvent()
    {
        return new FactionBalanceStateEvent(_cfg.GetCVar(RatCCVars.FactionBalanceEnabled),
            _cfg.GetCVar(RatCCVars.FactionBalanceAdminBypass),
            new Dictionary<string, FactionBalanceEntry>(_state));
    }

    private static bool Matches(
        Dictionary<string, FactionBalanceEntry> a,
        Dictionary<string, FactionBalanceEntry> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (faction, entry) in a)
        {
            if (!b.TryGetValue(faction, out var other) || other.Count != entry.Count || other.Cap != entry.Cap)
                return false;
        }

        return true;
    }

    #endregion
}
