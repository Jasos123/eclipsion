using Robust.Shared.GameStates;
using Robust.Shared.Utility;
using static Robust.Shared.Utility.SpriteSpecifier;

namespace Content.Shared._RMC14.Marines.Orders;

/// <summary>
/// Grants movement speed to entities affected by a Move order.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MoveOrderComponent : Component, IOrderComponent
{
    [DataField, AutoNetworkedField]
    public int Power { get; set; } = 1;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt { get; set; }

    [DataField, AutoNetworkedField]
    public SpriteSpecifier Icon = new Rsi(new ResPath("/Textures/_RMC14/Interface/marine_orders.rsi"), "move");

    /// <summary>
    /// Movement speed bonus per power level.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MoveSpeedModifier = 0.1f;
}
