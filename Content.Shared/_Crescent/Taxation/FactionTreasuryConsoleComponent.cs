using Robust.Shared.Audio;

namespace Content.Shared._Crescent.Taxation;

/// <summary>
/// A per-faction treasury console. Displays and dispenses the tax revenue accumulated in the
/// owning station's <c>StationTradeMarketComponent</c>. Access is gated by the console's
/// <c>AccessReader</c> (faction funds access): anyone without access cannot open the UI at all
/// and instead triggers a (rate-limited) intrusion alarm.
/// </summary>
[RegisterComponent]
public sealed partial class FactionTreasuryConsoleComponent : Component
{
    /// <summary>
    /// Faction key this vault belongs to (e.g. "DSM", "NCWL", "SHI"). The intrusion alert is sent
    /// only to this faction's members via the overwatch system. Empty disables the broadcast.
    /// </summary>
    [DataField]
    public string Faction = string.Empty;

    /// <summary>
    /// Sound played when someone without access tries to open the console.
    /// </summary>
    [DataField]
    public SoundSpecifier AlarmSound =
        new SoundPathSpecifier("/Audio/Machines/warning_buzzer.ogg");

    /// <summary>
    /// Minimum time between two intrusion-alarm plays. Set long (minutes) so the alarm sounds
    /// occasionally through a robbery to signal it's ongoing, without spamming the local sound.
    /// </summary>
    [DataField]
    public TimeSpan AlarmInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Minimum time between two sector-wide intrusion announcements. Much longer than the local
    /// alarm cooldown so a would-be thief can't spam a global broadcast.
    /// </summary>
    [DataField]
    public TimeSpan AnnounceCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum fraction (0..1) of the treasury any single faction member may withdraw in one round
    /// across all of this faction's consoles. Prevents one person draining the vault; the treasury
    /// keeps enough for the faction to actually use. Defaults to 50%.
    /// </summary>
    [DataField]
    public float MaxWithdrawFraction = 0.50f;

    /// <summary>
    /// Most credits taken from a cash stack per click when paying in.
    /// </summary>
    /// <remarks>
    /// A deposit used to bank the clicked stack's entire count and delete the item, so paying a small
    /// sum out of a large stack silently cost the whole stack. Capping the bite keeps deposits
    /// deliberate: click repeatedly to pay in more, and split the stack first to pay in less.
    /// </remarks>
    [DataField]
    public int DepositPerClick = 10_000;

    // --- Robbery ----------------------------------------------------------

    /// <summary>
    /// Fraction (0..1) of the vault's balance that a single robbery attempt siphons, paid out
    /// gradually over <see cref="RobberyDuration"/>. Each unauthorized click that finds the console
    /// idle starts a fresh heist for this share of whatever is in the vault at that moment.
    /// </summary>
    [DataField]
    public float RobberyFraction = 0.33f;

    /// <summary>How long one robbery attempt takes to pay out its full share.</summary>
    [DataField]
    public TimeSpan RobberyDuration = TimeSpan.FromMinutes(10);

    /// <summary>How often, while a robbery is running, the next chunk of cash is spilled out.</summary>
    [DataField]
    public TimeSpan RobberyTickInterval = TimeSpan.FromSeconds(20);

    // --- Runtime state (not authored in YAML) -----------------------------

    /// <summary>Server time the intrusion alarm last played, for throttling.</summary>
    [ViewVariables]
    public TimeSpan? LastAlarm;

    /// <summary>Server time the sector-wide intrusion announcement last fired.</summary>
    [ViewVariables]
    public TimeSpan? LastAnnounce;

    /// <summary>
    /// Total credits the current robbery will pay out (captured from the balance when it started).
    /// Zero when no robbery is active.
    /// </summary>
    [ViewVariables]
    public int RobberyTotal;

    /// <summary>Credits already spilled by the current robbery.</summary>
    [ViewVariables]
    public int RobberyDispensed;

    /// <summary>Server time the current robbery began, or null when the vault is idle/secure.</summary>
    [ViewVariables]
    public TimeSpan? RobberyStart;

    /// <summary>Server time the current robbery finishes paying out and stops on its own.</summary>
    [ViewVariables]
    public TimeSpan? RobberyEnd;

    /// <summary>Server time cash was last spilled by the active robbery, for tick pacing.</summary>
    [ViewVariables]
    public TimeSpan? LastLoot;
}
