using Content.Server.Power.EntitySystems;

namespace Content.Server.Power.Components;

/// <summary>
/// Adds a player-facing control UI to a power-network battery, such as a SMES or substation.
/// </summary>
[RegisterComponent, Access(typeof(PowerStorageControlSystem))]
public sealed partial class PowerStorageControlComponent : Component
{
    /// <summary>
    /// The highest input limit selectable in the UI. A value of zero uses the battery's map-init value.
    /// </summary>
    [DataField]
    public float MaxInputLimit;

    /// <summary>
    /// The highest output limit selectable in the UI. A value of zero uses the battery's map-init value.
    /// </summary>
    [DataField]
    public float MaxOutputLimit;

    [ViewVariables]
    public TimeSpan NextUiUpdate;
}
