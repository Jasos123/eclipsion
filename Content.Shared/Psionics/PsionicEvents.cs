namespace Content.Shared.Psionics;

/// <summary>
///     This event is raised whenever a psionic entity sets their casting stats(Amplification and Dampening), allowing other systems to modify the end result
///     of casting stat math. Useful if for example you want a species to have 5% higher Amplification overall. Or a drug inhibits total Dampening, etc.
/// </summary>
/// <param name="receiver"></param>
/// <param name="amplificationChangedAmount"></param>
/// <param name="dampeningChangedAmount"></param>
[ByRefEvent]
public record struct OnSetPsionicStatsEvent(float AmplificationChangedAmount, float DampeningChangedAmount);

[ByRefEvent]
public record struct OnMindbreakEvent();

/// <summary>
///     Raised on a psion immediately after they successfully use a power, carrying the amount of glimmer
///     that use pushed into the noosphere. Progression listens to this to award Potentia.
/// </summary>
/// <remarks>
///     This exists alongside PsionicPowerUsedEvent rather than reusing it because that pair is already
///     claimed by the metapsionic detection pass, and a component/event pair may only have one subscriber.
/// </remarks>
[ByRefEvent]
public record struct PsionicPowerCastEvent(EntityUid User, string Power, float Glimmer);
