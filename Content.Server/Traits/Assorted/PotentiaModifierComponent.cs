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
}
