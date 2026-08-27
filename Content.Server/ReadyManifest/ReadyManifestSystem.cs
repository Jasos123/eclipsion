using Content.Server.EUI;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Preferences;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared.ReadyManifest;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.GameTicking.Events;

namespace Content.Server.ReadyManifest;

public sealed class ReadyManifestSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IServerPreferencesManager _prefsManager = default!;

    private const float RefreshInterval = 1f;

    private readonly Dictionary<ICommonSession, ReadyManifestEui> _openEuis = new();
    private Dictionary<ProtoId<JobPrototype>, int> _jobCounts = new();
    private float _timeSinceRefresh;

    public override void Initialize()
    {
        SubscribeNetworkEvent<RequestReadyManifestMessage>(OnRequestReadyManifest);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<PlayerToggleReadyEvent>(OnPlayerToggleReady);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        foreach (var (_, eui) in _openEuis)
        {
            eui.Close();
        }

        _openEuis.Clear();
        _jobCounts.Clear();
        _timeSinceRefresh = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_openEuis.Count == 0)
        {
            _timeSinceRefresh = 0f;
            return;
        }

        _timeSinceRefresh += frameTime;
        if (_timeSinceRefresh < RefreshInterval)
            return;

        _timeSinceRefresh = 0f;
        if (BuildReadyManifest())
            UpdateEuis();
    }

    private void OnRequestReadyManifest(RequestReadyManifestMessage message, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not { } sessionCast
            || !_configManager.GetCVar(CCVars.CrewManifestWithoutEntity))
        {
            return;
        }
        BuildReadyManifest();
        OpenEui(sessionCast, args.SenderSession.AttachedEntity);
    }

    private void OnPlayerToggleReady(PlayerToggleReadyEvent ev)
    {
        // Rebuild from the authoritative ready-state snapshot. Incrementally changing the cache here can
        // drift when a player switches or edits their selected character while ready, since the profile
        // being removed is then no longer necessarily the profile that was originally counted.
        if (BuildReadyManifest())
            UpdateEuis();
    }

    private bool BuildReadyManifest()
    {
        var jobCounts = new Dictionary<ProtoId<JobPrototype>, int>();

        foreach (var (userId, status) in _gameTicker.PlayerGameStatuses)
        {
            if (status != PlayerGameStatus.ReadyToPlay ||
                !_prefsManager.TryGetCachedPreferences(userId, out var preferences) ||
                preferences.SelectedCharacter is not HumanoidCharacterProfile profile ||
                !TryGetManifestJob(profile.JobPriorities, out var jobId))
                continue;

            jobCounts[jobId] = jobCounts.GetValueOrDefault(jobId) + 1;
        }

        if (JobCountsMatch(jobCounts, _jobCounts))
            return false;

        _jobCounts = jobCounts;
        return true;
    }

    /// <summary>
    /// Gets the single job which represents this player in the manifest. A profile may have many low or
    /// medium fallback choices (including jobs in other factions), but validation guarantees at most one
    /// high-priority choice. Counting the fallback choices made one player appear several times.
    /// </summary>
    internal static bool TryGetManifestJob(
        IReadOnlyDictionary<string, JobPriority> priorities,
        out ProtoId<JobPrototype> jobId)
    {
        foreach (var (job, priority) in priorities)
        {
            if (priority != JobPriority.High)
                continue;

            jobId = new ProtoId<JobPrototype>(job);
            return true;
        }

        jobId = default;
        return false;
    }

    private static bool JobCountsMatch(
        IReadOnlyDictionary<ProtoId<JobPrototype>, int> first,
        IReadOnlyDictionary<ProtoId<JobPrototype>, int> second)
    {
        if (first.Count != second.Count)
            return false;

        foreach (var (job, count) in first)
        {
            if (!second.TryGetValue(job, out var other) || count != other)
                return false;
        }

        return true;
    }

    public Dictionary<ProtoId<JobPrototype>, int> GetReadyManifest()
    {
        return _jobCounts;
    }

    public void OpenEui(ICommonSession session, EntityUid? owner = null)
    {


        if (_openEuis.ContainsKey(session))
        {
            return;
        }

        var eui = new ReadyManifestEui(owner, this);
        _openEuis.Add(session, eui);
        _euiManager.OpenEui(eui, session);
        eui.StateDirty();
    }

    private void UpdateEuis()
    {
        foreach (var (_, eui) in _openEuis)
        {
            eui.StateDirty();
        }
    }

    /// <summary>
    ///     Closes an EUI for a given player.
    /// </summary>
    /// <param name="session">The player's session.</param>
    /// <param name="owner">The owner of this EUI, if there was one.</param>
    public void CloseEui(ICommonSession session, EntityUid? owner = null)
    {
        if (!_openEuis.TryGetValue(session, out var eui))
        {
            return;
        }

        if (eui.Owner == owner)
        {
            _openEuis.Remove(session);
            eui.Close();
        }
    }
}
