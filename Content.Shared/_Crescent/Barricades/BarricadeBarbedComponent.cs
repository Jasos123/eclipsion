using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Barricades;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BarricadeBarbedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsBarbed;

    [DataField]
    public EntProtoId WirePrototype = "CrescentBarbedWire";
}

[RegisterComponent]
public sealed partial class BarricadeWireComponent : Component;

[Serializable, NetSerializable]
public enum BarricadeVisuals : byte
{
    Barbed,
}
