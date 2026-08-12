using Robust.Shared.GameStates;

namespace Content.Shared.Abilities.Psionics;

/// <summary>
/// Passive ten-percent reinforcement granted by the defense skill tree.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PsionicArmorUpgradeComponent : Component;

/// <summary>
/// Temporary protection against heat and explosions. The server owns expiry and the attached visual.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class PsionicEnergyShieldComponent : Component
{
    [DataField]
    public float HeatCoefficient = 0.35f;

    [DataField]
    public float ExplosionCoefficient = 0.35f;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ExpiresAt;

    public EntityUid? Visual;
}

/// <summary>
/// Stores the object currently held by telekinetic manipulation.
/// </summary>
[RegisterComponent]
public sealed partial class TelekineticManipulationComponent : Component
{
    public EntityUid? SelectedObject;

    [DataField]
    public float MaximumMass = 60f;

    [DataField]
    public float MaximumRange = 8f;
}
