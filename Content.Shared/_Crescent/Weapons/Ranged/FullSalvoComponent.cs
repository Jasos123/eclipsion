using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Weapons.Ranged;

/// <summary>
/// Prevents a burst weapon from firing until it has enough ammunition for a full salvo.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FullSalvoComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public int RequiredShots;
}
