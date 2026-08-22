using Content.Server.NPC.Systems;

namespace Content.Server.NPC.Queries.Queries;

/// <summary>
/// Returns only the entity currently sitting in <see cref="NPCBlackboard.CurrentOrderedTarget"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="NearbyHostilesQuery"/> this asks nothing about factions or vision. An ordered
/// target was chosen by whoever controls the NPC, and the whole point of the order is that it
/// overrides what the NPC would have picked for itself - including going after something its own
/// faction considers friendly.
/// </remarks>
public sealed partial class OrderedTargetQuery : UtilityQuery
{

}
