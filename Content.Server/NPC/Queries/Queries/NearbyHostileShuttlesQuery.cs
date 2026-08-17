using Content.Server.NPC.Systems;
using Content.Shared.Whitelist;

// Mono - whole file

namespace Content.Server.NPC.Queries.Queries;

/// <summary>
/// Returns nearby shuttles considered hostile from <see cref="FactionSystem"/>
/// </summary>
public sealed partial class NearbyHostileShuttlesQuery : UtilityQuery
{
    [DataField]
    public float Range = 2000f;

    // Eclipsion - diplomacy-aware targeting, so an AI ship stops shooting its own side.
    /// <summary>
    /// How a candidate's diplomatic standing decides whether we shoot at it.
    /// </summary>
    [DataField]
    public ShipNpcTargeting Targeting = ShipNpcTargeting.Auto;

    [DataField]
    public EntityWhitelist Blacklist = new();
}

// Eclipsion - whole enum.
/// <summary>
/// How a ship NPC turns diplomacy into a target list.
/// </summary>
public enum ShipNpcTargeting : byte
{
    /// <summary>
    /// Diplomacy-aware when our own grid flies a faction we recognise, faction-blind when it does not. Keeps
    /// unaligned hostiles (derelict rammer cores and the like) attacking everything the way they always have,
    /// while a hull that belongs to somebody stops firing on its own side.
    /// </summary>
    Auto,

    /// <summary>
    /// Only factions we are at war with. An NPC with no faction of its own finds nothing to shoot, and neither
    /// does a target that has none - there is no relation to measure against a derelict or an asteroid.
    /// </summary>
    War,

    /// <summary>
    /// Every faction except our own and its allies. Like <see cref="War"/> this needs a faction on both sides,
    /// so unaligned traffic is still passed over; only <see cref="All"/> shoots at something with no faction.
    /// </summary>
    NonAlly,

    /// <summary>
    /// Ignore diplomacy entirely - anything in range is a target.
    /// </summary>
    All,
}
