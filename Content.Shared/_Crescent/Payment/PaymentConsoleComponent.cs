namespace Content.Shared._Crescent.Payment;

/// <summary>
/// A console that sets standing salaries for a faction's members, paid from that faction's treasury.
/// </summary>
/// <remarks>
/// Only server systems read this; it lives in Shared so prototypes can load it. Deliberately not
/// networked — everything the client renders arrives in <see cref="PaymentConsoleState"/>.
/// </remarks>
[RegisterComponent]
public sealed partial class PaymentConsoleComponent : Component
{
    /// <summary>
    /// Which faction's payroll and treasury this console administers. Set per prototype variant, the
    /// same way <c>OverwatchConsoleComponent.Faction</c> is.
    /// </summary>
    [DataField]
    public string Faction = string.Empty;

    /// <summary>
    /// Share of the vault (0..1) one operator may pay out in bonuses per round, counted against the
    /// same per-person budget as hand withdrawals at the treasury console.
    /// </summary>
    /// <remarks>
    /// Bonuses used to draw from the treasury uncapped, which meant the vault console's careful
    /// per-person limit could be sidestepped entirely: two colluding members could move 100% of the
    /// balance into bank accounts in seconds while the vault itself would have stopped them at half.
    /// Sharing one budget makes the limit mean something.
    /// </remarks>
    [DataField]
    public float MaxPayoutFraction = 0.50f;

    /// <summary>Minimum time between two bonus payments from this console.</summary>
    [DataField]
    public TimeSpan BonusCooldown = TimeSpan.FromSeconds(30);

    /// <summary>Server time a bonus was last paid from this console, for throttling.</summary>
    [ViewVariables]
    public TimeSpan? LastBonus;
}
