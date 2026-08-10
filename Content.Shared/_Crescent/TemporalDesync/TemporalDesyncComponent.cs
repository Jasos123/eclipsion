using Content.Shared._Crescent.Overlays;
using Content.Shared._Crescent.SpaceBiomes;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._Crescent.TemporalDesync;

/// <summary>
///     Tracks biological desynchronization from severe recurrence exposure.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TemporalDesyncComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DesyncLevel = 0.001f;
}

/// <summary>
///     Reduces how fast recurrence exposure desynchronizes the carrier.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DesyncResistanceComponent : Component
{
    [DataField, AutoNetworkedField]
    public float ResistanceMultiplier = 0.5f;
}
