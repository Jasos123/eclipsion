using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Crescent.Barricades;

[RegisterComponent]
public sealed partial class BarricadeUpgradeKitComponent : Component
{
    [DataField(required: true)]
    public string Upgrade = string.Empty;
}

[RegisterComponent]
public sealed partial class BarricadeUpgradeableComponent : Component
{
    [DataField]
    public Dictionary<string, EntProtoId> Upgrades = new();
}
