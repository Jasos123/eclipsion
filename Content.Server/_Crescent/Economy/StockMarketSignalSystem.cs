using Content.Server.Cargo.Systems;
using Content.Server.GameTicking.Events;
using Content.Server._Crescent.Taxation;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._Crescent.RoundEnd;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Economy;

/// <summary>
/// Translates what actually happens in a round into pressure on the stock market.
///
/// The four faction companies have no randomness of their own, so this system is the only thing that
/// moves them: a trader who reads the war correctly makes money, and one who guesses does not. Every
/// signal here is deliberately small — the intent is a market that drifts with the war's fortunes,
/// not one that swings wildly on any single event.
///
/// The two neutral companies are the other side of every trade: <c>fmn</c> profits from bloodshed and
/// <c>tccc</c> from rebuilding, so there is always somewhere to hide when the factions are all sinking.
/// </summary>
public sealed class StockMarketSignalSystem : EntitySystem
{
    [Dependency] private readonly StockCompanySystem _stocks = default!;
    [Dependency] private readonly FactionTreasurySystem _treasury = default!;
    [Dependency] private readonly OfflineFactionProtectionSystem _protection = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const string MerchantsId = "stock-company-fmn";
    private const string ConstructionId = "stock-company-tccc";

    /// <summary>Faction tag as written on mobs and stations, mapped to the company that tracks it.</summary>
    private static readonly IReadOnlyDictionary<string, string> FactionCompanies = FactionStockCompanies.ByFaction;

    /// <summary>
    /// Share lost by a faction when its entire living strength dies once over. Divided by the number
    /// of people it still has standing, so one death out of forty barely registers while one death
    /// out of three is a real blow.
    /// </summary>
    private const float DeathImpact = 0.03f;

    /// <summary>Fraction of a faction's loss that the merchants pick up. War is good for business.</summary>
    private const float MerchantWarProfit = 0.2f;

    private const int DeathDuration = 4;

    /// <summary>A lost station is the single biggest in-round move available.</summary>
    private const float StationFallImpact = 0.04f;
    private const int StationFallDuration = 10;

    /// <summary>Rebuilding contracts. Smaller than the fall itself and slower to arrive.</summary>
    private const float ConstructionRebuildProfit = 0.02f;
    private const int ConstructionRebuildDuration = 20;

    /// <summary>Winning the war. Applied instantly because pending pressure is dropped at round end.</summary>
    private const float VictoryImpact = 0.06f;

    private const float MissionImpact = 0.01f;
    private const int MissionDuration = 8;

    private static readonly TimeSpan TreasuryPollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Cap on how much one treasury reading may move a company, either way.</summary>
    private const float TreasuryMaxImpact = 0.004f;

    /// <summary>How much of a treasury's relative change carries into share pressure.</summary>
    private const float TreasuryWeight = 0.02f;

    private const int TreasuryDuration = 4;

    /// <summary>Smoothing on the treasury baseline, so a single big purchase is not read as a collapse.</summary>
    private const float TreasuryEmaAlpha = 0.25f;

    private static readonly TimeSpan RosterScanInterval = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, float> _treasuryBaseline = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Factions seen fielded at any point this round. This only ever grows: a faction whose station is
    /// blown up has emphatically not left the market, it has just had a very bad day. Whether its stock
    /// is currently tradeable is decided separately by offline protection.
    /// </summary>
    private readonly HashSet<string> _fielded = new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _nextTreasuryPoll;
    private TimeSpan _nextRosterScan;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FactionStationFellEvent>(OnStationFell);
        SubscribeLocalEvent<FactionWarDecidedEvent>(OnWarDecided);
        SubscribeLocalEvent<FactionMissionCompletedEvent>(OnMissionCompleted);
        // Deliberately NOT on RoundRestartCleanupEvent. StockCompanySystem applies its between-round
        // recovery on that same event and needs the finishing round's listing flags to still be intact;
        // system ordering within one event is not guaranteed, so delisting waits for the new round.
        SubscribeLocalEvent<RoundStartingEvent>(_ => ResetRound());

        _nextTreasuryPoll = _timing.CurTime + TreasuryPollInterval;
        _nextRosterScan = _timing.CurTime;
    }

    private void ResetRound()
    {
        // Baselines are round state: last round's treasury is not this round's starting point.
        _treasuryBaseline.Clear();
        _fielded.Clear();

        // Everyone is out until they show up. A mode that fields two powers trades two.
        _stocks.DeactivateFactionCompanies();

        _nextTreasuryPoll = _timing.CurTime + TreasuryPollInterval;
        _nextRosterScan = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now >= _nextRosterScan)
        {
            _nextRosterScan = now + RosterScanInterval;
            ScanRoster();
        }

        if (now < _nextTreasuryPoll)
            return;

        _nextTreasuryPoll = now + TreasuryPollInterval;
        PollTreasuries();
    }

    /// <summary>
    /// Finds factions represented by the mode, then lists only the ones whose player activity makes
    /// their persistent economy vulnerable this round.
    /// </summary>
    private void ScanRoster()
    {
        var stations = EntityQueryEnumerator<FactionStationComponent>();
        while (stations.MoveNext(out _, out var station))
            MarkFielded(station.Faction);

        // Crews without a headquarters still count; some modes field a faction as ships only.
        var mobs = EntityQueryEnumerator<HullrotFactionComponent>();
        while (mobs.MoveNext(out _, out var member))
            MarkFielded(member.Faction);

        _protection.RefreshActivity();
        foreach (var (faction, companyId) in FactionCompanies)
        {
            var active = _fielded.Contains(faction) && !_protection.IsProtected(faction);
            _stocks.SetActive(companyId, active);
        }
    }

    private void MarkFielded(string faction)
    {
        if (!string.IsNullOrEmpty(faction))
            _fielded.Add(faction);
    }

    /// <summary>
    /// Compares each faction's treasury against its own smoothed baseline and nudges its company in
    /// the same direction. This is the market's slow background trend — a faction that is quietly
    /// getting richer drifts up even in a round where nobody dies.
    /// </summary>
    private void PollTreasuries()
    {
        foreach (var (faction, companyId) in FactionCompanies)
        {
            if (!_fielded.Contains(faction))
                continue;

            var balance = _treasury.Get(faction);

            if (!_treasuryBaseline.TryGetValue(faction, out var baseline))
            {
                // First reading of the round is the reference, not a signal.
                _treasuryBaseline[faction] = balance;
                continue;
            }

            // A treasury sitting at zero has no meaningful "relative" change to report.
            if (baseline > 1f)
            {
                var relative = (balance - baseline) / baseline;
                var impact = Math.Clamp(relative * TreasuryWeight, -TreasuryMaxImpact, TreasuryMaxImpact);

                if (MathF.Abs(impact) > 0.0001f)
                {
                    var reason = impact > 0f
                        ? Loc.GetString("stock-news-treasury-up", ("faction", faction))
                        : Loc.GetString("stock-news-treasury-down", ("faction", faction));

                    _stocks.AddPressure(companyId, impact, TreasuryDuration, reason);
                }
            }

            _treasuryBaseline[faction] = baseline + (balance - baseline) * TreasuryEmaAlpha;
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // Crit and revival both fire this; only an actual death is a casualty.
        if (args.NewMobState != MobState.Dead || args.OldMobState == MobState.Dead)
            return;

        if (!TryComp<HullrotFactionComponent>(args.Target, out var faction))
            return;

        if (!FactionCompanies.TryGetValue(faction.Faction, out var companyId))
            return;

        var living = CountLiving(faction.Faction);
        var impact = DeathImpact / Math.Max(1, living);

        _stocks.AddPressure(
            companyId,
            -impact,
            DeathDuration,
            Loc.GetString("stock-news-casualties", ("faction", faction.Faction)));

        _stocks.AddPressure(
            MerchantsId,
            impact * MerchantWarProfit,
            DeathDuration,
            Loc.GetString("stock-news-merchant-war"));
    }

    /// <summary>
    /// How many of a faction's people are still on their feet. The dead mob is already in its new
    /// state by the time this runs, so it is not counted.
    /// </summary>
    private int CountLiving(string faction)
    {
        var count = 0;

        var query = EntityQueryEnumerator<HullrotFactionComponent, MobStateComponent>();
        while (query.MoveNext(out _, out var member, out var mobState))
        {
            if (mobState.CurrentState == MobState.Dead)
                continue;

            if (string.Equals(member.Faction, faction, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private void OnStationFell(ref FactionStationFellEvent args)
    {
        if (!FactionCompanies.TryGetValue(args.Faction, out var companyId))
            return;

        _stocks.AddPressure(
            companyId,
            -StationFallImpact,
            StationFallDuration,
            Loc.GetString("stock-news-station-lost", ("station", args.StationName)));

        _stocks.AddPressure(
            ConstructionId,
            ConstructionRebuildProfit,
            ConstructionRebuildDuration,
            Loc.GetString("stock-news-rebuild", ("station", args.StationName)));
    }

    private void OnWarDecided(ref FactionWarDecidedEvent args)
    {
        foreach (var winner in args.Winners)
        {
            if (!FactionCompanies.TryGetValue(winner, out var companyId))
                continue;

            _stocks.AddInstantShift(
                companyId,
                VictoryImpact,
                Loc.GetString("stock-news-victory", ("faction", winner)));
        }
    }

    private void OnMissionCompleted(ref FactionMissionCompletedEvent args)
    {
        if (!FactionCompanies.TryGetValue(args.Faction, out var companyId))
            return;

        _stocks.AddPressure(
            companyId,
            MissionImpact,
            MissionDuration,
            Loc.GetString("stock-news-contract", ("faction", args.Faction)));
    }
}
