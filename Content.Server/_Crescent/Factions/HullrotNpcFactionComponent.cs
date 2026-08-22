namespace Content.Server._Crescent.Factions;

/// <summary>
///     Bookkeeping for <see cref="HullrotNpcFactionSyncSystem"/>: which npcFaction id it put on this mob.
/// </summary>
/// <remarks>
///     Kept so a faction change can take the old membership back off again. It has to be remembered rather than
///     derived, because by the time we are asked to change it the Hullrot faction field already holds the new
///     value and there is nothing left to say what the previous one was.
/// </remarks>
[RegisterComponent]
public sealed partial class HullrotNpcFactionComponent : Component
{
    [DataField]
    public string? Applied;
}
