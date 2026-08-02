using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Chemistry.Components;

/// <summary>
/// Lets a container be flipped between drawing from and pouring into whatever it's clicked on. Without this,
/// anything both refillable and drainable (fuel tanks) picks a direction for you and usually the wrong one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SolutionTransferDirectionComponent : Component
{
    [DataField, AutoNetworkedField]
    public SolutionTransferDirection Direction = SolutionTransferDirection.Receive;
}

[Serializable, NetSerializable]
public enum SolutionTransferDirection : byte
{
    Receive,
    Send,
}
