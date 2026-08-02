namespace Content.Server.Crescent.Dispenser;

[RegisterComponent]
public sealed partial class StationTradeMarketComponent : Component
{
    [DataField]
    public Dictionary<string, float> SalesAccumulator = new();

    [DataField]
    public float PriceDropPerSale = 0.02f;

    [DataField]
    public float MinMultiplier = 0.3f;


    [DataField]
    public float RecoveryRatePerSecond = 1f / 60f;

    // --- Taxation ---------------------------------------------------------

    /// <summary>
    /// Station-wide default tax rate (0..1) applied to every trade good sold through
    /// this station's trade points, unless a per-good override exists in <see cref="TaxOverrides"/>.
    /// Set from the taxation console.
    /// </summary>
    [DataField]
    public float DefaultTaxRate = 0f;

    /// <summary>
    /// Per-trade-good tax rate overrides (0..1), keyed by trade good prototype id.
    /// Takes precedence over <see cref="DefaultTaxRate"/> for that specific good.
    /// </summary>
    [DataField]
    public Dictionary<string, float> TaxOverrides = new();

    /// <summary>
    /// Hard ceiling on any tax rate so a console operator can never confiscate the
    /// entire payout (which would make trading pointless).
    /// </summary>
    [DataField]
    public float MaxTaxRate = 0.95f;

    /// <summary>
    /// Tax revenue for an <b>unaligned</b> station only, and per-round only.
    /// </summary>
    /// <remarks>
    /// A station belonging to a faction does not keep a balance here: its money lives in
    /// <c>FactionTreasurySystem</c> under <see cref="Faction"/>, because a faction routinely owns
    /// several stations at once (its home station plus every shipyard-bought hull that becomes its own
    /// station) and a per-station copy of the balance both duplicated and destroyed money as the copies
    /// wrote over each other. Read and write through <c>StationTradeMarketSystem</c>, never this field.
    /// </remarks>
    [DataField]
    public int TreasuryBalance = 0;

    /// <summary>
    /// Faction key whose treasury this station banks into (e.g. "SHI", "NCWL", "DSM"), resolved from
    /// the station grid's IFF faction. Empty means unaligned: the station keeps its own per-round
    /// balance in <see cref="TreasuryBalance"/> instead.
    /// </summary>
    [ViewVariables]
    public string Faction = string.Empty;

    /// <summary>
    /// Whether <see cref="Faction"/> has been resolved this round. Guards the lookup so it runs once
    /// per station rather than every tick, and makes the first writer win.
    /// </summary>
    [ViewVariables]
    public bool FactionResolved = false;
}
