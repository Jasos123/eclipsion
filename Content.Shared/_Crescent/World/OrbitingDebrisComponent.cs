using System.Numerics;

namespace Content.Shared._Crescent.World;

/// <summary>
/// Moves worldgen debris around a fixed center.
/// </summary>
[RegisterComponent]
public sealed partial class OrbitingDebrisComponent : Component
{
    /// <summary>
    /// Time for one revolution.
    /// </summary>
    [DataField]
    public TimeSpan Period = TimeSpan.FromHours(4);

    /// <summary>
    /// Center of the orbit.
    /// </summary>
    [DataField]
    public Vector2 Center = Vector2.Zero;

    /// <summary>
    /// Debris inside this radius remains stationary.
    /// </summary>
    [DataField]
    public float MinRadius = 3000f;

    [DataField]
    public bool Clockwise;

    /// <summary>
    /// Whether physics setup has completed.
    /// </summary>
    [ViewVariables]
    public bool Started;
}
