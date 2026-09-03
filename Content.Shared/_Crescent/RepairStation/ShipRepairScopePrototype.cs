using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._Crescent.RepairStation;

/// <summary>
/// Declares what an automated repair slip is allowed to put back on a hull.
/// </summary>
/// <remarks>
/// This is deliberately separate from <c>ShipRepairableComponent</c>, which is the hand-held repair
/// device's scope. The drydock keeps its own server-side file of the hull, so widening this costs the
/// device nothing and adds nothing to what the server networks to clients.
/// Every prototype of this type is consulted; an anchored structure is on file if any of them takes it.
/// </remarks>
[Prototype("shipRepairScope")]
public sealed partial class ShipRepairScopePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// What this scope covers. Null means every anchored structure on the hull, which is the point of
    /// the slip - the blacklist is what narrows it back down.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Structures this scope refuses, whatever the whitelist said. This is where anything that would
    /// hand out free contents on being rebuilt belongs.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Wreckage the slip may clear off a tile before rebuilding what stood there - the girder a wall
    /// leaves behind, the frame a machine leaves behind. Without this the slip welds a new wall
    /// straight through the girder and the tile ends up holding both.
    /// </summary>
    /// <remarks>
    /// Keep this to bare frames. Anything finished that lands on the list gets torn down whenever the
    /// blueprint disagrees with it, which is vandalism rather than repair.
    /// </remarks>
    [DataField]
    public EntityWhitelist? Clear;

    /// <summary>
    /// Loose wreckage the slip sweeps off a hull it is putting back together - the sheets a wall
    /// sheds, the shards a window sheds, the coil a cut cable leaves behind. Nothing anchored is
    /// judged by this list, and nothing inside a container or a pocket is either.
    /// </summary>
    /// <remarks>
    /// Keep this to what the hull itself shed on its way to pieces. Anything a crewman might have put
    /// down on purpose gets binned along with the wreckage whenever a repair is authorised.
    /// </remarks>
    [DataField]
    public EntityWhitelist? Debris;
}
