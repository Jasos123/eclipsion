using System.Linq;
using System.Text.Json;
using Content.Shared.Cargo.Cartridges;
using Content.Shared.GameTicking;
using Robust.Shared.ContentPack;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Cargo.Systems;

/// <summary>
/// The sector stock market. Every listed company holds a slice of a single pool of market share that
/// always sums to 100%, so a company can only gain what the others lose — there is no free money in
/// the market as a whole, only reallocation.
///
/// The four faction companies carry no randomness whatsoever: their price moves only when something
/// actually happens in the round (casualties, a station going dark, treasury swings, a won war).
/// Reading the war correctly is the entire skill of trading them. The two neutral companies
/// (<c>fmn</c>, <c>tccc</c>) are the exception — they carry a little noise, pull back toward their
/// opening price, and profit from the chaos the factions generate, so there is always something to
/// hedge into.
///
/// The authoritative per-company state is its <see cref="StockCompanyData.PriceMultiplier"/>, not its
/// share. Share is derived from the multipliers of whichever companies are currently listed. That
/// distinction matters: a mode that fields only two factions delists the other two, and if share were
/// the stored quantity, renormalising the smaller pool would jolt every remaining price at round start
/// for no in-game reason. Storing the multiplier means the roster can change without moving a price.
///
/// Market state survives round restarts (in memory) and server restarts (mirrored to JSON in the
/// server's user-data directory), so a faction that spent three rounds losing starts the fourth cheap.
/// </summary>
public sealed class StockCompanySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IResourceManager _res = default!;

    private static readonly ResPath SavePath = new("/stock_market.json");
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    /// <summary>Bumped when the save format changes so stale files are dropped instead of misread.</summary>
    private const int SaveVersion = 2;

    private const float UpdateInterval = StockMarketTrading.TickSeconds;

    public const int MaxHistoryLength = 120;

    /// <summary>How far a company's price may drift from where it opened.</summary>
    public const float MinMultiplier = 0.25f;
    private const float MaxMultiplier = 2.5f;

    /// <summary>Per-tick pull back toward the opening price. Only the two neutral companies get this.</summary>
    private const float NeutralReversionStrength = 0.02f;

    /// <summary>
    /// Price movement is deliberately asymmetric: every upward push lands at <see cref="RiseFactor"/> of
    /// its intended strength while every downward one lands at <see cref="FallFactor"/>. Building a
    /// stock's value up is a slow grind and losing it is quick, so holding a winning position is work
    /// rather than a coast. These are the tuning knobs for that feel — closer together is gentler, wider
    /// apart is harsher.
    ///
    /// This scales only the *intended* shifts, never the redistribution that funds them, so the pool
    /// stays exactly conserved. Note it also touches the neutral companies' own drift, giving the two
    /// hedges a mild downward lean; move the scaling into <see cref="AddPressure"/>/<see cref="AddInstantShift"/>
    /// instead if the hedges should stay symmetric.
    /// </summary>
    private const float RiseFactor = 0.7f;
    private const float FallFactor = 1.2f;

    /// <summary>
    /// Fraction of the gap back to the opening price that a listed company closes when a new round
    /// begins. Without it a faction that loses repeatedly pins itself to the floor and never trades
    /// again; with it, a bad run is a discount rather than a death sentence.
    /// </summary>
    private const float RoundRecovery = 0.2f;

    private readonly List<StockCompanyData> _companies = new();
    private readonly List<StockNewsRecord> _news = new();

    /// <summary>How many news entries are kept for the cartridge feed.</summary>
    public const int MaxNews = 30;

    private TimeSpan _lastUpdate;
    private bool _initialized;
    private bool _dirty;
    private float _sinceSave;

    /// <summary>Set by the admin freeze command; pauses price movement without clearing state.</summary>
    public bool Frozen;

    public override void Initialize()
    {
        base.Initialize();
        _lastUpdate = _timing.CurTime;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        EnsureInitialized();
        Load();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Prices persist across rounds; only the per-round scratch state resets.
        foreach (var company in _companies)
        {
            company.Pressures.Clear();

            // Only companies that actually traded this round recover. A delisted company is frozen in
            // every sense — it comes back at exactly the price it crashed to. You recover by playing.
            if (!company.Active)
                continue;

            company.PriceMultiplier += (1f - company.PriceMultiplier) * RoundRecovery;
        }

        _news.Clear();
        Normalize();

        _lastUpdate = _timing.CurTime;
        _dirty = true;

        // Written after the recovery, not before, so a server killed at the round boundary reloads
        // the state the next round would actually have started from.
        Save();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        _companies.Clear();

        // (loc id, base price, opening share, volatility, neutral)
        // Opening shares must sum to exactly 1.0 — they are the weights the pool is divided by.
        _companies.Add(new StockCompanyData("stock-company-shi", 500f, 0.20f, 0f, neutral: false));
        _companies.Add(new StockCompanyData("stock-company-tfsc", 400f, 0.18f, 0f, neutral: false));
        _companies.Add(new StockCompanyData("stock-company-dsm", 350f, 0.16f, 0f, neutral: false));
        _companies.Add(new StockCompanyData("stock-company-ncwl", 250f, 0.14f, 0f, neutral: false));
        _companies.Add(new StockCompanyData("stock-company-fmn", 300f, 0.18f, 0.004f, neutral: true));
        _companies.Add(new StockCompanyData("stock-company-tccc", 180f, 0.14f, 0.002f, neutral: true));

        Normalize();

        foreach (var company in _companies)
            company.PriceHistory.Add(company.CurrentPrice);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        EnsureInitialized();

        if (_dirty)
        {
            _sinceSave += frameTime;
            if (_sinceSave >= SaveInterval.TotalSeconds)
                Save();
        }

        if (Frozen)
            return;

        var curTime = _timing.CurTime;
        if ((curTime - _lastUpdate).TotalSeconds < UpdateInterval)
            return;

        _lastUpdate = curTime;

        Tick();
    }

    private void Tick()
    {
        // Collect every company's intended relative change first, then settle them against each other
        // in one pass. Applying them one at a time would let whoever is first in the list skim value
        // off the others before they have moved.
        var shifts = new float[_companies.Count];

        for (var i = 0; i < _companies.Count; i++)
        {
            var company = _companies[i];
            if (!company.Active)
                continue;

            var shift = ConsumePressure(company);

            if (company.Neutral)
            {
                shift += company.Volatility * NextGaussian();
                shift += (1f / company.PriceMultiplier - 1f) * NeutralReversionStrength;
            }

            shifts[i] = shift;
        }

        ApplyShifts(shifts);

        foreach (var company in _companies)
        {
            company.PriceHistory.Add(company.CurrentPrice);
            if (company.PriceHistory.Count > MaxHistoryLength)
                company.PriceHistory.RemoveAt(0);
        }

        _dirty = true;

        var ev = new StockCompaniesUpdatedEvent();
        RaiseLocalEvent(ref ev);
    }

    /// <summary>
    /// What a raw magnitude is actually worth once the rise/fall asymmetry has been applied. Public so
    /// the trading screen can quote committed pressure at the size it will really land, rather than the
    /// size the signal system asked for — a trader told "-2%" who then watches -2.4% arrive has been
    /// misled by the display, not by the market.
    /// </summary>
    public static float EffectiveShift(float magnitude)
    {
        return magnitude * (magnitude >= 0f ? RiseFactor : FallFactor);
    }

    /// <summary>Advances every running pressure on a company by one tick and returns their combined shift.</summary>
    private static float ConsumePressure(StockCompanyData company)
    {
        if (company.Pressures.Count == 0)
            return 0f;

        var total = 0f;
        for (var i = company.Pressures.Count - 1; i >= 0; i--)
        {
            var pressure = company.Pressures[i];
            total += pressure.ShiftPerTick;

            pressure.RemainingTicks--;
            if (pressure.RemainingTicks <= 0)
                company.Pressures.RemoveAt(i);
            else
                company.Pressures[i] = pressure;
        }

        return total;
    }

    /// <summary>
    /// The total weight the listed companies share out between them. Equals the sum of their opening
    /// shares, so with everyone at their opening price every multiplier is exactly 1.
    /// </summary>
    private float ListedWeightTarget()
    {
        var target = 0f;
        foreach (var company in _companies)
        {
            if (company.Active)
                target += company.InitialShare;
        }
        return target;
    }

    /// <summary>
    /// Settles a set of relative changes against the common pool. A company asking for +2% takes that
    /// weight from the other listed companies in proportion to what they hold, so the pool's total is
    /// conserved and the shares still add to 100%.
    /// </summary>
    private void ApplyShifts(float[] shifts)
    {
        // Make gains grind and losses bite: dampen every upward push, amplify every downward one. Done
        // here rather than at each call site so it catches war pressures, instant shifts and the neutral
        // companies' own drift in one place, and before any weight is computed so the pool stays conserved.
        for (var i = 0; i < shifts.Length; i++)
            shifts[i] = EffectiveShift(shifts[i]);

        var weights = new float[_companies.Count];
        var totalWeight = 0f;

        for (var i = 0; i < _companies.Count; i++)
        {
            var company = _companies[i];
            if (!company.Active)
                continue;

            weights[i] = company.InitialShare * company.PriceMultiplier;
            totalWeight += weights[i];
        }

        if (totalWeight <= 0f)
        {
            Normalize();
            return;
        }

        var gains = new float[_companies.Count];
        var totalGain = 0f;

        for (var i = 0; i < _companies.Count; i++)
        {
            if (!_companies[i].Active)
                continue;

            gains[i] = weights[i] * shifts[i];
            totalGain += gains[i];
        }

        for (var i = 0; i < _companies.Count; i++)
        {
            var company = _companies[i];
            if (!company.Active)
                continue;

            var funding = totalGain * (weights[i] / totalWeight);
            company.PriceMultiplier = (weights[i] + gains[i] - funding) / company.InitialShare;
        }

        Normalize();
    }

    /// <summary>
    /// Clamps every price into its allowed band, rescales the listed companies so the pool's weight is
    /// exactly conserved, then rebuilds the derived prices and percentages.
    /// </summary>
    private void Normalize()
    {
        foreach (var company in _companies)
            company.PriceMultiplier = Math.Clamp(company.PriceMultiplier, MinMultiplier, MaxMultiplier);

        var target = ListedWeightTarget();
        var sum = 0f;
        foreach (var company in _companies)
        {
            if (company.Active)
                sum += company.InitialShare * company.PriceMultiplier;
        }

        if (target > 0f && sum > 0f)
        {
            var scale = target / sum;
            foreach (var company in _companies)
            {
                if (!company.Active)
                    continue;

                // Clamped again after scaling, not just before it. The rescale is what conserves the pool, but
                // it moves every listed company by the same factor, so a company already sitting on the floor
                // gets pushed under it whenever the others are pinned at the ceiling. A price below
                // MinMultiplier would put the market below GetFloorPrice, and war liquidation — which pays the
                // floor precisely so it stings — would start paying out *more* than selling on the open market.
                company.PriceMultiplier = Math.Clamp(company.PriceMultiplier * scale, MinMultiplier, MaxMultiplier);
            }
        }

        RecalculateDerived();
    }

    /// <summary>
    /// Rebuilds price and share from the multipliers. Share is presentation only — a delisted company
    /// reports 0% because it is not part of the pool being divided, while its price stays where it was.
    /// </summary>
    private void RecalculateDerived()
    {
        var target = ListedWeightTarget();

        foreach (var company in _companies)
        {
            company.CurrentPrice = company.BasePrice * company.PriceMultiplier;
            company.PriceChange = company.PriceMultiplier - 1.0f;
            company.Share = company.Active && target > 0f
                ? company.InitialShare * company.PriceMultiplier / target
                : 0f;
        }
    }

    private float NextGaussian()
    {
        var u1 = MathF.Max(_random.NextFloat(), 1e-6f);
        var u2 = _random.NextFloat();
        return MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
    }

    #region Public API

    /// <summary>
    /// Pushes a company by <paramref name="magnitude"/> (relative to its own weight, so 0.02 is
    /// "+2%") spread evenly over <paramref name="durationTicks"/> ticks. Whatever it gains is taken
    /// from the rest of the listed market and vice versa.
    /// </summary>
    /// <param name="reason">Shown to players in the cartridge news feed. Keep it short.</param>
    public bool AddPressure(string companyId, float magnitude, int durationTicks, string reason)
    {
        EnsureInitialized();

        // NaN fails every comparison, so `magnitude == 0f` alone would let it through and poison the pool
        // permanently. float.TryParse accepts "NaN", so the admin shock command can produce one.
        if (durationTicks <= 0 || !float.IsFinite(magnitude) || magnitude == 0f)
            return false;

        var company = _companies.Find(c => c.Id == companyId);
        if (company == null)
            return false;

        company.Pressures.Add(new StockPressure(magnitude / durationTicks, durationTicks));

        if (!string.IsNullOrWhiteSpace(reason))
            PushNews(companyId, reason, magnitude);

        _dirty = true;
        return true;
    }

    /// <summary>
    /// Moves a company immediately instead of spreading it over ticks. Used for things that have to
    /// land before the round ends (a won war), since pending pressures are dropped on restart.
    /// </summary>
    public bool AddInstantShift(string companyId, float magnitude, string reason)
    {
        EnsureInitialized();

        var index = _companies.FindIndex(c => c.Id == companyId);
        if (index < 0 || !float.IsFinite(magnitude) || magnitude == 0f)
            return false;

        var shifts = new float[_companies.Count];
        shifts[index] = magnitude;
        ApplyShifts(shifts);

        if (!string.IsNullOrWhiteSpace(reason))
            PushNews(companyId, reason, magnitude);

        _dirty = true;
        var ev = new StockCompaniesUpdatedEvent();
        RaiseLocalEvent(ref ev);
        return true;
    }

    private void PushNews(string companyId, string reason, float magnitude)
    {
        _news.Add(new StockNewsRecord(
            companyId,
            reason,
            magnitude > 0f,
            _timing.CurTime,
            EffectiveShift(magnitude)));

        while (_news.Count > MaxNews)
            _news.RemoveAt(0);
    }

    /// <summary>
    /// Lists or delists a company. Delisting freezes its price and pulls it out of the pool entirely;
    /// because prices are stored as multipliers rather than shares, doing so does not move anybody
    /// else's price — it only changes the percentages the remaining companies are quoted at.
    /// </summary>
    public void SetActive(string companyId, bool active)
    {
        EnsureInitialized();

        var company = _companies.Find(c => c.Id == companyId);
        if (company == null || company.Active == active)
            return;

        company.Active = active;
        company.Pressures.Clear();

        RecalculateDerived();

        _dirty = true;
        var ev = new StockCompaniesUpdatedEvent();
        RaiseLocalEvent(ref ev);
    }

    /// <summary>
    /// Delists every faction company. The signal system relists the ones whose faction actually turns
    /// up, so a mode that fields only two powers trades only those two.
    /// </summary>
    public void DeactivateFactionCompanies()
    {
        EnsureInitialized();

        foreach (var company in _companies)
        {
            if (company.Neutral)
                continue;

            company.Active = false;
            company.Pressures.Clear();
        }

        RecalculateDerived();
        _dirty = true;
    }

    public List<StockCompanyData> GetCompanies()
    {
        EnsureInitialized();
        return _companies;
    }

    /// <summary>
    /// Everything already committed to move this company but not yet paid out: the net shift still to
    /// come, and how many ticks the longest-running pressure has left to run.
    ///
    /// The market screen quotes this because the alternative is worse. A casualty pushes a company down
    /// over four ticks, so a trader who checks the price a minute later sees it still falling with the
    /// news that caused it already scrolled away, and concludes the whole thing is random. Showing the
    /// committed move turns that into a decision: sell now, or ride it out.
    /// </summary>
    public (float Shift, int Ticks) GetPendingPressure(StockCompanyData company)
    {
        var shift = 0f;
        var ticks = 0;

        foreach (var pressure in company.Pressures)
        {
            shift += pressure.ShiftPerTick * pressure.RemainingTicks;
            ticks = Math.Max(ticks, pressure.RemainingTicks);
        }

        // Netted first, then scaled: two opposing pressures that cancel out are worth nothing, and
        // quoting each leg through the asymmetry separately would invent a downward move from nowhere.
        return (EffectiveShift(shift), ticks);
    }

    public StockCompanyData? GetCompany(string id)
    {
        EnsureInitialized();
        return _companies.Find(c => c.Id == id);
    }

    /// <summary>
    /// The lowest price this company can ever trade at. A forced sale pays this rather than the market
    /// price — that is what makes being liquidated out of an enemy's stock a loss and not an exit.
    /// </summary>
    public float GetFloorPrice(StockCompanyData company) => company.BasePrice * MinMultiplier;

    public List<StockNewsRecord> GetNews()
    {
        EnsureInitialized();
        return _news;
    }

    /// <summary>
    /// Forces a company to a given price, funded by the other listed companies so the pool stays
    /// balanced. Admin use only — this bypasses the pressure system and lands immediately.
    /// </summary>
    public bool SetPrice(string companyId, double price)
    {
        EnsureInitialized();

        // IsFinite rather than just a range check: NaN fails every comparison, so `price <= 0` would wave it
        // through, and a single NaN multiplier propagates through the pool and gets written to the save file.
        var target = _companies.Find(c => c.Id == companyId);
        if (target == null || target.BasePrice <= 0f || !double.IsFinite(price) || price <= 0d)
            return false;

        target.PriceMultiplier = Math.Clamp((float) (price / target.BasePrice), MinMultiplier, MaxMultiplier);

        // Absorb the difference across the *other* listed companies, so the admin gets exactly the
        // price they asked for instead of one that has been scaled away underneath them.
        if (target.Active)
        {
            var poolTarget = ListedWeightTarget();
            var othersTarget = poolTarget - target.InitialShare * target.PriceMultiplier;
            var othersSum = 0f;

            foreach (var company in _companies)
            {
                if (company.Active && company != target)
                    othersSum += company.InitialShare * company.PriceMultiplier;
            }

            if (othersTarget > 0f && othersSum > 0f)
            {
                var scale = othersTarget / othersSum;
                foreach (var company in _companies)
                {
                    if (company.Active && company != target)
                        company.PriceMultiplier = Math.Clamp(company.PriceMultiplier * scale, MinMultiplier, MaxMultiplier);
                }
            }
        }

        RecalculateDerived();

        _dirty = true;
        var ev = new StockCompaniesUpdatedEvent();
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Wipes all market state back to opening prices. Admin use only.</summary>
    public void ResetMarket()
    {
        EnsureInitialized();

        foreach (var company in _companies)
        {
            company.Pressures.Clear();
            company.PriceMultiplier = 1f;
            company.PriceHistory.Clear();
        }

        _news.Clear();
        Normalize();

        foreach (var company in _companies)
            company.PriceHistory.Add(company.CurrentPrice);

        _dirty = true;
        Save();

        var ev = new StockCompaniesUpdatedEvent();
        RaiseLocalEvent(ref ev);
    }

    #endregion

    #region Persistence

    private void Load()
    {
        try
        {
            if (!_res.UserData.TryReadAllText(SavePath, out var json))
                return;

            var saved = JsonSerializer.Deserialize<StockMarketSaveData>(json);
            if (saved is null || saved.Version != SaveVersion)
                return;

            foreach (var company in _companies)
            {
                if (!saved.Companies.TryGetValue(company.Id, out var state))
                    continue;

                // A corrupt or hand-edited file must not be able to poison the pool.
                if (!float.IsFinite(state.Multiplier) || state.Multiplier <= 0f)
                    continue;

                company.PriceMultiplier = state.Multiplier;

                company.PriceHistory.Clear();
                if (state.PriceHistory != null)
                {
                    foreach (var price in state.PriceHistory.Where(float.IsFinite))
                        company.PriceHistory.Add(price);
                }
            }

            Normalize();

            foreach (var company in _companies)
            {
                if (company.PriceHistory.Count == 0)
                    company.PriceHistory.Add(company.CurrentPrice);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load stock market state: {e}");
        }
    }

    private void Save()
    {
        _sinceSave = 0f;
        if (!_dirty)
            return;

        try
        {
            var data = new StockMarketSaveData { Version = SaveVersion };
            foreach (var company in _companies)
            {
                data.Companies[company.Id] = new StockCompanySaveState
                {
                    Multiplier = company.PriceMultiplier,
                    PriceHistory = new List<float>(company.PriceHistory),
                };
            }

            _res.UserData.WriteAllText(SavePath, JsonSerializer.Serialize(data));
            _dirty = false;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save stock market state: {e}");
        }
    }

    #endregion
}

public sealed class StockCompanyData
{
    public readonly string Id;
    public readonly float BasePrice;

    /// <summary>
    /// The slice this company holds when every listed company is at its opening price. Also the weight
    /// it carries in the pool, which is what makes the shares add up to 100%.
    /// </summary>
    public readonly float InitialShare;

    /// <summary>Random per-tick drift. Zero for the faction companies — they move on events only.</summary>
    public readonly float Volatility;

    /// <summary>Neutral companies drift and mean-revert; faction companies do neither.</summary>
    public readonly bool Neutral;

    /// <summary>
    /// Whether this company's faction is actually fielded this round. A company that is not is held
    /// completely out of the market: its price is frozen, it neither funds nor receives redistribution,
    /// and it is not offered for trade. Otherwise an absent faction would drift upward for free every
    /// time the present ones took losses, which is money for nothing.
    /// </summary>
    public bool Active = true;

    /// <summary>
    /// The authoritative state. Price is this times <see cref="BasePrice"/>; share is derived from it.
    /// Stored rather than share so that listing or delisting a company never moves anyone's price.
    /// </summary>
    public float PriceMultiplier = 1.0f;

    /// <summary>Derived: this company's slice of the listed pool, 0 to 1. Listed companies sum to 1.</summary>
    public float Share;

    public float CurrentPrice;
    public float PriceChange;

    public readonly List<StockPressure> Pressures = new();
    public readonly List<float> PriceHistory = new();

    public StockCompanyData(string id, float basePrice, float initialShare, float volatility, bool neutral)
    {
        Id = id;
        BasePrice = basePrice;
        InitialShare = initialShare;
        Share = initialShare;
        Volatility = volatility;
        Neutral = neutral;
        CurrentPrice = basePrice;
    }
}

/// <summary>A running push on a company, spread over a number of ticks.</summary>
public struct StockPressure
{
    public float ShiftPerTick;
    public int RemainingTicks;

    public StockPressure(float shiftPerTick, int remainingTicks)
    {
        ShiftPerTick = shiftPerTick;
        RemainingTicks = remainingTicks;
    }
}

internal sealed class StockMarketSaveData
{
    public int Version { get; set; }
    public Dictionary<string, StockCompanySaveState> Companies { get; set; } = new();
}

internal sealed class StockCompanySaveState
{
    public float Multiplier { get; set; }
    public List<float>? PriceHistory { get; set; }
}

[ByRefEvent]
public readonly record struct StockCompaniesUpdatedEvent;
