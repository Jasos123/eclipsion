using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.RoundEnd;

/// <summary>
/// Marks a station grid as a faction's seat of power for the conquest win condition. Only stations carrying this
/// count — a faction can hold any number of other grids and still be eliminated once its marked stations fall.
///
/// Applied through the gameMap prototype's <c>gridComponents</c>, so which station "counts" is decided per map
/// rather than by IFF faction. That matters for the TFCF, whose distinct member organizations share a Federation
/// gameplay faction and common infrastructure for the win condition.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FactionStationComponent : Component
{
    /// <summary>Faction id that owns this station (matches HullrotFaction / diplomacy ids: DSM, NCWL, SHI, TFSC...).</summary>
    [DataField(required: true)]
    public string Faction = string.Empty;

    /// <summary>
    /// Broadcast once, in bold orange, the moment this station goes dark for good. Written per station so the
    /// sector hears which hull fell rather than a generic notice.
    /// </summary>
    [DataField]
    public string? FallAnnouncement;

    /// <summary>
    /// What to call this station in the blackout warning and the round end summary. Set explicitly because the
    /// component sits on the grid, whose entity name is whatever the mapper happened to save.
    /// </summary>
    [DataField]
    public string StationName = "the station";
}
