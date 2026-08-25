namespace Content.Server.Traits.Assorted;

/// <summary>
///     This is used for traits that modify the outcome of Potentia rolls.
/// </summary>
[RegisterComponent]
public sealed partial class PotentiaModifierComponent : Component
{
    /// <summary>
    ///     Increase Potentia gained from a progression roll by a flat amount.
    /// </summary>
    [DataField]
    public float PotentiaFlatModifier = 0;

    /// <summary>
    ///     When rolling for psionic powers, multiply the potentia gains by a specific factor.
    /// </summary>
    [DataField]
    public float PotentiaMultiplier = 1;

    /// <summary>
    ///     Multiplies the Potentia earned in-round from casting powers and from ambient glimmer. The two
    ///     fields above only touch the one-shot progression roll, which is a small slice of a Psion's
    ///     career now that levels are earned by playing the role. Species put their aptitude here because
    ///     the PsionicComponent that carries the Psion's own multiplier is handed out by a caster trait,
    ///     long after the species prototype has been built.
    /// </summary>
    [DataField]
    public float PotentiaGainMultiplier = 1;
}
