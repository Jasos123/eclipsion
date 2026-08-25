using Content.Shared.NPC.Systems;

namespace Content.Server.NPC.Queries.Queries;

/// <summary>
/// Eclipsion - returns nearby mobs that are sealed against vacuum and are not friendly to us.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately does not go through <see cref="NpcFactionSystem"/>'s hostile sets the way
/// <see cref="NearbyHostilesQuery"/> does. Those sets only ever match an entity that carries an
/// npcFaction of its own, so every unaligned boarder - a mercenary, a privateer flying under no
/// flag, anyone a recruitment console never touched - was invisible to an anti-boarder gun no
/// matter how obviously they were kicking the airlock in. Here anything that is not positively
/// friendly counts, so the gun no longer needs the sector to have declared a war first.
/// </para>
/// <para>
/// The sealed-suit requirement is the other half of that trade: it is what keeps a gun that now
/// sees everyone from mowing down the shirtsleeve crew walking past it.
/// </para>
/// </remarks>
public sealed partial class NearbyBoardersQuery : UtilityQuery
{
}
