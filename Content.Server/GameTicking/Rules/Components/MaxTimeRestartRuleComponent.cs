using Robust.Shared.Audio;

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Configures the <see cref="InactivityTimeRestartRuleSystem"/> game rule.
/// </summary>
[RegisterComponent]
public sealed partial class MaxTimeRestartRuleComponent : Component
{
    /// <summary>
    /// The max amount of time the round can last
    /// </summary>
    [DataField("roundMaxTime", required: true)]
    public TimeSpan RoundMaxTime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The amount of time between the round completing and the lobby appearing.
    /// </summary>
    [DataField("roundEndDelay", required: true)]
    public TimeSpan RoundEndDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Crescent - announcements broadcast on the way to the cap, so the round does not just stop dead on the crew.
    /// Left empty the rule behaves exactly as it always did.
    /// </summary>
    [DataField]
    public List<RoundTimeWarning> Warnings = new();

    /// <summary>
    /// Crescent - absolute <see cref="Robust.Shared.Timing.IGameTiming.CurTime"/> the round is due to end at.
    /// Null means the cap is disarmed: either the round is not running, or an admin cancelled/paused it. Admin
    /// commands move this rather than cancelling and respawning a timer, so the deadline stays inspectable.
    /// </summary>
    [ViewVariables]
    public TimeSpan? EndTime;

    /// <summary>
    /// Crescent - how much was left on the clock when an admin paused it. Non-null only while paused, and
    /// <see cref="EndTime"/> is always null alongside it.
    /// </summary>
    [ViewVariables]
    public TimeSpan? PausedRemaining;

    /// <summary>
    /// Crescent - CurTime the last tick looked at, so a warning fires exactly on the tick its instant is crossed.
    /// Deriving "fired" from the crossing rather than a flag means an admin who extends the round past a warning
    /// it already passed gets that warning again on the way to the new deadline, which is what the crew needs.
    /// </summary>
    [ViewVariables]
    public TimeSpan LastCheck;
}

/// <summary>
/// Crescent - one scheduled heads-up before <see cref="MaxTimeRestartRuleComponent.RoundMaxTime"/> runs out.
/// </summary>
[DataDefinition]
public sealed partial class RoundTimeWarning
{
    /// <summary>
    /// How long before the cap this goes out. An entry longer than the round itself simply never fires.
    /// </summary>
    [DataField(required: true)]
    public TimeSpan Before;

    /// <summary>
    /// Locale id of the announcement body. Gets <c>$minutes</c> filled in from <see cref="Before"/>.
    /// </summary>
    [DataField(required: true)]
    public LocId Message;

    /// <summary>
    /// Locale id of the announcer name shown in front of the message.
    /// </summary>
    [DataField]
    public LocId? Sender;

    [DataField]
    public Color Color = Color.Orange;

    [DataField]
    public SoundSpecifier? Sound;
}
