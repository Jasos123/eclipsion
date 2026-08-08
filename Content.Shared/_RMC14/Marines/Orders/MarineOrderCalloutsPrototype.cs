using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Marines.Orders;

/// <summary>
/// Faction-specific lines shouted when an order is issued.
/// The ID must match the issuer's <see cref="Content.Shared._Crescent.HullrotFaction.HullrotFactionComponent.Faction"/>,
/// such as "NCWL" or "DSM". Factions without their own set use the generic callouts on
/// <see cref="MarineOrdersComponent"/>.
/// </summary>
[Prototype("marineOrderCallouts")]
public sealed partial class MarineOrderCalloutsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<LocId> Move = new();

    [DataField]
    public List<LocId> Hold = new();

    [DataField]
    public List<LocId> Focus = new();
}

public enum MarineOrderType : byte
{
    Move,
    Hold,
    Focus,
}
