using Robust.Shared.Map;

namespace Content.Server.Explosion;

/// <summary>
/// Raised when an incendiary explosion is queued, allowing content systems to create persistent fire.
/// </summary>
public sealed class ExplosionFireEvent(
    EntityCoordinates epicenter,
    float radius,
    float fireStacks) : EntityEventArgs
{
    public EntityCoordinates Epicenter { get; } = epicenter;
    public float Radius { get; } = radius;
    public float FireStacks { get; } = fireStacks;
}
