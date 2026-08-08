using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Shared.Utility.SpriteSpecifier;

namespace Content.Shared._RMC14.Marines.Orders;

/// <summary>
/// Grants damage resistance to entities affected by a Hold order.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HoldOrderComponent : Component, IOrderComponent
{
    [DataField, AutoNetworkedField]
    public int Power { get; set; } = 1;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt { get; set; }

    [DataField, AutoNetworkedField]
    public SpriteSpecifier Icon = new Rsi(new ResPath("/Textures/_RMC14/Interface/marine_orders.rsi"), "hold");

    /// <summary>
    /// Damage reduction per power level.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DamageModifier = 0.05f;

    /// <summary>
    /// Damage types affected by the order.
    /// </summary>
    [DataField]
    public List<ProtoId<DamageTypePrototype>> DamageTypes = new() { "Slash", "Blunt", "Piercing" };
}
