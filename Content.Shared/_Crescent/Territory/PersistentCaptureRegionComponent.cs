using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Territory;

/// <summary>
/// The major powers allowed to participate in persistent territory control. The faction prototype still uses the
/// legacy <c>TFSC</c> ID for the Taypani Free Companies Federation (TFCF).
/// </summary>
public static class PersistentTerritoryFactions
{
    public static bool IsSupported(string? faction)
    {
        return faction is "DSM" or "NCWL" or "TFSC" or "SHI";
    }
}

/// <summary>
/// Turns a <see cref="Content.Shared.CaptureFlag.CaptureFlagComponent"/> into a freeplay territory whose owner
/// survives round and server restarts. The flag must sit on the grid that represents the territory on radar.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PersistentCaptureRegionComponent : Component
{
    /// <summary>
    /// Stable save key. It must remain unique across every persistent region and must not be changed when its map is
    /// renamed, moved or remapped.
    /// </summary>
    [DataField(required: true)]
    public string RegionId = string.Empty;

    /// <summary>
    /// Faction-independent radar name. Ownership is displayed as "FACTION BaseName".
    /// If omitted, the grid's name at first application is used.
    /// </summary>
    [DataField]
    public string? BaseName;

    /// <summary>
    /// Preferred radar colours for factions that capture this region. Missing factions use their faction prototype's
    /// button colour.
    /// </summary>
    [DataField]
    public Dictionary<string, Color> FactionColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DSM"] = Color.FromHex("#C677A5"),
        ["NCWL"] = Color.Orange,
        ["SHI"] = Color.White,
        ["TFSC"] = Color.IndianRed,
    };

    /// <summary>Radar colour used while nobody owns the region.</summary>
    [DataField]
    public Color NeutralColor = Color.Gold;

    /// <summary>Flag sprite states keyed by faction ID.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, string> TeamStates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether holding this region periodically benefits the owner's listed faction stock.</summary>
    [DataField]
    public bool StockRewardEnabled = true;

    /// <summary>Seconds between passive stock rewards. Ten minutes keeps one region's effect deliberately small.</summary>
    [DataField]
    public float StockRewardInterval = 600f;

    /// <summary>
    /// Relative stock pressure per reward. 0.001 is +0.1% raw pressure before the market's normal rise damping.
    /// </summary>
    [DataField]
    public float StockRewardMagnitude = 0.001f;

    /// <summary>Market ticks over which one reward is applied.</summary>
    [DataField]
    public int StockRewardDuration = 4;

    /// <summary>
    /// Last owner whose territory effects were applied. Runtime-only: persisted ownership lives in the system save.
    /// </summary>
    [ViewVariables]
    public string? AppliedOwner;

    /// <summary>Next passive reward time. Runtime-only and reset when the map loads or ownership changes.</summary>
    [ViewVariables]
    public TimeSpan NextStockReward;

    /// <summary>False when map validation rejected this region ID. Runtime-only.</summary>
    [ViewVariables]
    public bool ValidRegion = true;
}

/// <summary>
/// Marks a machine or anti-boarder turret as controlled by a persistent capture region.
/// </summary>
[RegisterComponent]
public sealed partial class CaptureRegionDeviceComponent : Component
{
    /// <summary>
    /// Optional explicit region key. Empty binds the device to the persistent region flag on its current grid.
    /// Set this when a territory spans multiple grids.
    /// </summary>
    [DataField]
    public string RegionId = string.Empty;

    /// <summary>Reassign a FactionMachine component when control changes.</summary>
    [DataField]
    public bool UpdateMachineFaction = true;

    /// <summary>Replace an NpcFactionMember component's factions when control changes.</summary>
    [DataField]
    public bool UpdateNpcFaction = true;
}

/// <summary>
/// Gives a capturable targeting console an automatic guard mode when nobody is operating its UI.
/// </summary>
[RegisterComponent]
public sealed partial class TerritoryAutoDefenseComponent : Component
{
    /// <summary>Maximum hostile-grid acquisition distance in world tiles.</summary>
    [DataField]
    public float Range = 2000f;

    /// <summary>
    /// Non-allied factions that are not in a declared war are warned inside <see cref="Range"/>, but are only
    /// engaged after crossing this distance.
    /// </summary>
    [DataField]
    public float NeutralEngagementRange = 1500f;

    /// <summary>Seconds between target searches and console-to-cannon link refreshes.</summary>
    [DataField]
    public float ScanInterval = 1f;

    /// <summary>If true, only factions in a declared war are engaged rather than every non-ally.</summary>
    [DataField]
    public bool WarOnly = true;

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public EntityUid? TargetGrid;

    [ViewVariables]
    public TimeSpan NextScan;
}
