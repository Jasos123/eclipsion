using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Weapons.Ranged;

/// <summary>
/// Gives a crew-served LMG its bipod accuracy bonus while it is wielded.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BarricadeLmgComponent : Component
{
    [DataField]
    public Angle MinAngleBonus = Angle.FromDegrees(-20);

    [DataField]
    public Angle MaxAngleBonus = Angle.FromDegrees(-35);

    [DataField]
    public Angle AngleIncreaseBonus = Angle.FromDegrees(-3);

    [DataField]
    public Angle AngleDecayBonus = Angle.FromDegrees(12);

    [DataField]
    public float CameraRecoilMultiplier = 0.35f;
}

[Serializable, NetSerializable]
public enum BarricadeLmgVisuals : byte
{
    Deployed,
}
