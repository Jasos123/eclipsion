using System.Numerics;

namespace Content.Client.Psionics;

/// <summary>
/// The client's own record of a predicted round it is holding inside a recurrence field, mirroring
/// what <c>TemporallySlowedComponent</c> records for the real one on the server.
/// </summary>
/// <remarks>
/// Client-side only, and never named in a prototype: it is only ever added in code to the throwaway
/// projectile the shooter's client spawns for itself.
/// </remarks>
[RegisterComponent]
public sealed partial class PredictedProjectileStasisComponent : Component
{
    /// <summary>
    /// The field holding this round.
    /// </summary>
    public EntityUid Field;

    /// <summary>
    /// The same field by network id. A pulse deletes the field in the same breath as it announces
    /// itself, so by the time the announcement is handled the local entity may already be gone;
    /// this is what the held rounds are matched against instead.
    /// </summary>
    public NetEntity FieldNet;

    /// <summary>
    /// Velocity at the moment of capture, measured against the field. Restored on release so the
    /// round leaves at the speed it arrived with rather than at whatever the scaling left it doing.
    /// </summary>
    public Vector2 EntryVelocity;

    /// <summary>
    /// The multiplier that was applied, taken from the field so both sides crawl at one rate.
    /// </summary>
    public float AppliedScale = 1f;
}
