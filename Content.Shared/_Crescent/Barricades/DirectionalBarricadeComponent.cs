using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Barricades;

/// <summary>
/// Lets projectiles fired from the protected side pass while blocking incoming fire.
/// The entity's local facing direction is considered the exposed side.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DirectionalBarricadeComponent : Component
{
    [DataField]
    public float PassDotThreshold = 0.1f;

    /// <summary>
    /// How far onto the protected side the shooter must be for its projectile to pass.
    /// This prevents a projectile that was fired outside (or directly on top of the barricade)
    /// from being admitted based on velocity alone.
    /// </summary>
    [DataField]
    public float ProtectedSideMargin = 0.05f;
}
