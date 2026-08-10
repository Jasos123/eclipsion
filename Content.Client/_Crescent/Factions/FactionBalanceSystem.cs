using Content.Client.Administration.Managers;
using Content.Shared._Crescent.Factions.FactionBalance;

namespace Content.Client._Crescent.Factions;

/// <summary>
/// Client mirror of the population-scaled join caps. Holds the latest snapshot the server pushed so the
/// late-join window can lock the jobs a player would be refused and say why.
/// </summary>
public sealed class FactionBalanceSystem : SharedFactionBalanceSystem
{
    [Dependency] private readonly IClientAdminManager _admin = default!;

    private bool _enabled;
    private bool _adminBypass;
    private Dictionary<string, FactionBalanceEntry> _factions = new();

    /// <summary>
    /// Raised when a fresh snapshot arrives, so open windows can refresh.
    /// </summary>
    public event Action? Updated;

    /// <summary>
    /// Current headcounts and caps, for display.
    /// </summary>
    public IReadOnlyDictionary<string, FactionBalanceEntry> Factions => _factions;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FactionBalanceStateEvent>(OnStateReceived);
        _admin.AdminStatusUpdated += OnAdminStatusUpdated;
    }

    public override void Shutdown()
    {
        _admin.AdminStatusUpdated -= OnAdminStatusUpdated;
        base.Shutdown();
    }

    private void OnStateReceived(FactionBalanceStateEvent ev)
    {
        _enabled = ev.Enabled;
        _adminBypass = ev.AdminBypass;
        _factions = ev.Factions;
        Updated?.Invoke();
    }

    private void OnAdminStatusUpdated()
    {
        // The exemption is evaluated from the local admin's active/deadmin state. Refresh any
        // open late-join window even when the server-side population snapshot did not change.
        Updated?.Invoke();
    }

    /// <summary>
    /// Whether this job's faction is currently full. The server has the final say; this only spares the
    /// player a button press that would be refused.
    /// </summary>
    public bool IsJobBlocked(string jobId, out string faction, out FactionBalanceEntry entry)
    {
        faction = string.Empty;
        entry = default;

        if (!_enabled || IsExempt() || !TryGetJobFaction(jobId, out faction))
            return false;

        return _factions.TryGetValue(faction, out entry) && entry.Full;
    }

    /// <summary>
    /// Mirrors the server's exemption so an admin who would be admitted does not see the job greyed
    /// out. Deadminned admins are not exempt, which is what IsActive already means.
    /// </summary>
    private bool IsExempt() => _adminBypass && _admin.IsActive();
}
