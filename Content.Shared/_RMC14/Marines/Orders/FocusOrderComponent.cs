using Robust.Shared.GameStates;
using Robust.Shared.Utility;
using static Robust.Shared.Utility.SpriteSpecifier;

namespace Content.Shared._RMC14.Marines.Orders;

/// <summary>
/// Reduces weapon spread for entities affected by a Focus order.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FocusOrderComponent : Component, IOrderComponent
{
    [DataField, AutoNetworkedField]
    public int Power { get; set; } = 1;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt { get; set; }

    [DataField, AutoNetworkedField]
    public SpriteSpecifier Icon = new Rsi(new ResPath("/Textures/_RMC14/Interface/marine_orders.rsi"), "focus");

    /// <summary>
    /// Spread reduction per power level.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SpreadReduction = 0.12f;

    /// <summary>
    /// Minimum spread multiplier.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinSpreadMultiplier = 0.4f;
}
