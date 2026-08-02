using System.Text.Json;
using Content.Server.Administration.Logs;
using Content.Server.Bank;
using Content.Server.Crescent.Dispenser;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._Crescent.Payment;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Crescent.Payment;

/// <summary>
/// A member's standing salary, and the sub-credit remainder carried between payouts.
/// </summary>
public sealed class PayrollEntry
{
    public int SalaryPerHour { get; set; }

    /// <summary>
    /// Earned but not yet paid out, in credits. Must be fractional: a payout tick is a fraction of an
    /// hour, so in integer maths any salary below one credit per tick would floor to zero forever.
    /// </summary>
    public double Accrued { get; set; }
}

/// <summary>
/// Pays standing salaries to faction members out of their faction's treasury.
/// </summary>
/// <remarks>
/// Rosters are keyed by <see cref="NetUserId"/> rather than entity, so an assignment survives
/// respawning, cloning and reconnecting, and persist across rounds like the treasury they spend from.
/// </remarks>
public sealed class FactionPayrollSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly StationTradeMarketSystem _market = default!;

    private static readonly ResPath SavePath = new("/faction_payroll.json");

    /// <summary>How often, at most, the roster is flushed to disk while dirty.</summary>
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Seconds between payout runs.
    /// </summary>
    /// <remarks>
    /// One minute, not one hour. On an hourly tick the entire wage went to whoever happened to be
    /// online at that single instant: log in five seconds before it and collect a full hour, play
    /// fifty-nine minutes and log off with nothing. Accruing a sixtieth of the wage each minute pays
    /// people for the time they actually served, and makes payday continuous rather than a lottery
    /// keyed to server uptime.
    /// </remarks>
    private const float PayoutInterval = 60f;

    /// <summary>
    /// Back-pay ceiling, in hours of salary. Unpaid wages accrue while the treasury is short, but
    /// only up to this much: without a cap a faction that leaves its vault empty builds an unbounded,
    /// cross-round liability that drains the whole balance the instant it is next funded.
    /// </summary>
    private const double MaxAccruedHours = 4.0;

    /// <summary>faction -> user -> entry.</summary>
    [ViewVariables]
    private readonly Dictionary<string, Dictionary<NetUserId, PayrollEntry>> _rosters = new();

    private bool _dirty;
    private float _sinceSave;
    private float _sincePayout;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Save());

        Load();
    }

    /// <summary>Standing salary for a member, or null if they aren't on the faction's payroll.</summary>
    public PayrollEntry? GetEntry(string faction, NetUserId user)
    {
        return _rosters.TryGetValue(faction, out var roster) && roster.TryGetValue(user, out var entry)
            ? entry
            : null;
    }

    public IReadOnlyDictionary<NetUserId, PayrollEntry> GetRoster(string faction)
    {
        return _rosters.TryGetValue(faction, out var roster)
            ? roster
            : new Dictionary<NetUserId, PayrollEntry>();
    }

    /// <summary>Sets a member's hourly salary. A salary of zero or less removes them from the payroll.</summary>
    public void SetSalary(string faction, NetUserId user, int salaryPerHour)
    {
        if (string.IsNullOrEmpty(faction))
            return;

        if (salaryPerHour <= 0)
        {
            ClearSalary(faction, user);
            return;
        }

        var roster = _rosters.GetOrNew(faction);
        var entry = roster.GetOrNew(user);
        entry.SalaryPerHour = Math.Min(salaryPerHour, PaymentConsole.MaxSalaryPerHour);
        _dirty = true;
    }

    public void ClearSalary(string faction, NetUserId user)
    {
        if (!_rosters.TryGetValue(faction, out var roster) || !roster.Remove(user))
            return;

        if (roster.Count == 0)
            _rosters.Remove(faction);

        _dirty = true;
    }

    /// <summary>
    /// Whether paying this member would actually stick. A bank balance is only written back to the
    /// character profile from <c>ComponentGetState</c> while the mob is attached to its player, and
    /// <c>BankSystem.OnMindAdded</c> resets the balance from the profile on return — so crediting a
    /// detached mob destroys the money. Members who fail this are skipped entirely and their salary
    /// stays in the treasury rather than accruing against it.
    /// </summary>
    public bool IsPayable(ICommonSession session, EntityUid mob)
    {
        return session.Status == SessionStatus.InGame && session.AttachedEntity == mob;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_dirty)
        {
            _sinceSave += frameTime;
            if (_sinceSave >= SaveInterval.TotalSeconds)
                Save();
        }

        _sincePayout += frameTime;
        if (_sincePayout < PayoutInterval)
            return;

        _sincePayout = 0f;
        RunPayout();
    }

    /// <summary>One member due wages this run.</summary>
    private readonly record struct PayoutCandidate(EntityUid Mob, string Faction, PayrollEntry Entry, int Owed);

    private void RunPayout()
    {
        if (_rosters.Count == 0)
            return;

        // Resolved once per run rather than per member; a faction's treasury doesn't move mid-tick.
        var stations = new Dictionary<string, EntityUid?>();
        var candidates = new List<PayoutCandidate>();

        // Own query rather than Overwatch's member cache: that one is keyed by entity and its
        // invalidation is tied to a UI being open, neither of which suits payroll.
        var query = EntityQueryEnumerator<HullrotFactionComponent, ActorComponent>();
        while (query.MoveNext(out var mob, out var factionComp, out var actor))
        {
            var faction = factionComp.Faction;
            if (string.IsNullOrEmpty(faction))
                continue;

            // A member who left the faction keeps a stale roster entry; it just never matches here.
            if (!_rosters.TryGetValue(faction, out var roster))
                continue;

            var session = actor.PlayerSession;
            if (!roster.TryGetValue(session.UserId, out var entry) || entry.SalaryPerHour <= 0)
                continue;

            if (!IsPayable(session, mob))
                continue;

            if (!stations.TryGetValue(faction, out var station))
            {
                station = _market.TryGetFactionTreasuryStation(faction);
                stations[faction] = station;
            }

            if (station == null)
                continue;

            entry.Accrued = Math.Min(
                entry.Accrued + entry.SalaryPerHour * (PayoutInterval / 3600.0),
                entry.SalaryPerHour * MaxAccruedHours);
            _dirty = true;

            var owed = (int) entry.Accrued;
            if (owed <= 0)
                continue;

            candidates.Add(new PayoutCandidate(mob, faction, entry, owed));
        }

        if (candidates.Count == 0)
            return;

        // Smallest debt first. When the treasury cannot cover everyone the order decides who eats the
        // shortfall, and raw entity-query order made that arbitrary and unrepeatable — the same roster
        // could pay different people on different runs. Paying the cheapest claims first also settles
        // the most members from a thin vault, and the rest keep their accrual for the next tick.
        candidates.Sort((a, b) => a.Owed.CompareTo(b.Owed));

        foreach (var (mob, faction, entry, owed) in candidates)
        {
            if (stations[faction] is not { } station)
                continue;

            // Clamped to the balance, and returns what it actually took — pay partial, never negative.
            var paid = _market.TryWithdrawTreasury(station, owed);
            if (paid <= 0)
                continue;

            if (!_bank.TryBankDeposit(mob, paid))
            {
                // Crediting failed after the treasury was debited. Put it back rather than burn it.
                _market.AddTreasury(station, paid);
                continue;
            }

            entry.Accrued -= paid;

            _adminLogger.Add(LogType.ATMUsage, LogImpact.Medium,
                $"Payroll: {faction} paid {paid} to {ToPrettyString(mob):player}");
        }
    }

    private void Load()
    {
        try
        {
            if (!_res.UserData.TryReadAllText(SavePath, out var json))
                return;

            var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, PayrollEntry>>>(json);
            if (loaded is null)
                return;

            _rosters.Clear();
            foreach (var (faction, roster) in loaded)
            {
                foreach (var (rawUser, entry) in roster)
                {
                    if (!Guid.TryParse(rawUser, out var guid))
                        continue;

                    _rosters.GetOrNew(faction)[new NetUserId(guid)] = entry;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load faction payroll: {e}");
        }
    }

    private void Save()
    {
        _sinceSave = 0f;
        if (!_dirty)
            return;

        try
        {
            // NetUserId isn't a JSON dictionary key, so rosters are written keyed by the raw GUID.
            var dto = new Dictionary<string, Dictionary<string, PayrollEntry>>();
            foreach (var (faction, roster) in _rosters)
            {
                var inner = new Dictionary<string, PayrollEntry>();
                foreach (var (user, entry) in roster)
                    inner[user.UserId.ToString()] = entry;

                dto[faction] = inner;
            }

            _res.UserData.WriteAllText(SavePath, JsonSerializer.Serialize(dto));
            _dirty = false;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save faction payroll: {e}");
        }
    }
}
