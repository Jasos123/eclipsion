
namespace Content.Server.Psionics;

/// <summary>
///     Raised on an entity about to roll for Potentia, after its baseline gain is calculated.
/// </summary>
[ByRefEvent]
public record struct OnRollPsionicsEvent(EntityUid Roller, float BaselineChance);
