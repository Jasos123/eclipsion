using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Shuttles.Components;

/// <summary>
/// Marks that this grid is currently mass cloaked by a specific console.
/// This component is used to track which console is doing the cloaking for visibility calculations.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MassCloakedByComponent : Component
{
    /// <summary>
    /// The UID of the console entity that is cloaking this grid.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid CloakingConsoleUid;

    /// <summary>
    /// The range of the cloaking field. Used to check if off-grid viewers are in the field.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float CloakingRange = 100f;

    /// <summary>
    /// Whether the mass-cloak system added the grid's IFF hide flag.
    /// This prevents leaving a field from disabling a grid's independently activated cloak.
    /// </summary>
    [ViewVariables]
    public bool HideFlagSetByMassCloak;
}
