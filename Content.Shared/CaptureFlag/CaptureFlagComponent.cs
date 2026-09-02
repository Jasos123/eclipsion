using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.CaptureFlag;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CaptureFlagComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Radius = 2.0f;

    [DataField, AutoNetworkedField]
    public float CaptureTime = 45f;

    [DataField, AutoNetworkedField]
    public float NeutralizeTime = 45f;

    [DataField]
    public bool DecayWhenInactive = true;

    [DataField]
    public float DecayRate = 1f;

    [DataField, AutoNetworkedField]
    public string? OwnerTeam;

    [DataField]
    public string NeutralState = "neutral-white";

    [DataField]
    public string DsmState = "black-purple";

    [DataField]
    public string NcwlState = "brown-ncwl";

    [DataField, AutoNetworkedField]
    public string? ActiveTeam;

    /// <summary>
    /// Team which owns the current partial progress. Kept separately from <see cref="ActiveTeam"/> so progress can
    /// decay while nobody is present without being inherited by the next faction that enters the radius.
    /// </summary>
    [ViewVariables]
    public string? ProgressTeam;

    [DataField, AutoNetworkedField]
    public float ProgressSeconds = 0f;

    [DataField, AutoNetworkedField]
    public CaptureFlagStage Stage = CaptureFlagStage.Idle;

    [DataField, AutoNetworkedField]
    public bool DominationEnabled = true;

    [DataField, AutoNetworkedField]
    public float DominationHoldTime = 900f;
}

[Serializable, NetSerializable]
public enum CaptureFlagStage : byte
{
    Idle = 0,
    Neutralizing = 1,
    Capturing = 2,
    Contested = 3
}

