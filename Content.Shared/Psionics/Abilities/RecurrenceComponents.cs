using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared.Abilities.Psionics;

/// <summary>
/// A suspended pocket of slowed time. Anything that drifts inside - bullets, thrown items, live
/// grenades, people - has its motion and its timers dragged down to a crawl until it leaves again
/// or the field collapses.
/// </summary>
/// <remarks>
/// The field entity itself is inert: it has no fixtures and never collides. Capture is driven by a
/// server-side range query, and the client only needs the radius so its overlay knows how much of
/// the world to drain of colour.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class RecurrenceFieldComponent : Component
{
    /// <summary>
    /// Radius of the field in metres. Also drives the size of the client-side greyscale overlay.
    /// </summary>
    /// <remarks>
    /// This has to agree with what the player is looking at. The field sprite is 128px at 32px to
    /// the metre, so it draws four metres across, and the PointLight on the prototype is set to
    /// match. A capture radius smaller than that is invisible and reads as the power being broken:
    /// a round crosses the drawn circle, passes outside the radius the whole way, is never a
    /// candidate, and carries on into the wall exactly as though nothing were there.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public float Radius = 2f;

    /// <summary>
    /// Fraction of its speed a captured object keeps. Release gives back the speed it was caught
    /// with, so a projectile that crosses the field leaves as fast as it arrived.
    /// </summary>
    /// <remarks>
    /// At this scale a rifle round takes the better part of four seconds to cross, which is the
    /// point: you are meant to be able to stand and watch it hang there. Objects that carry
    /// friction - thrown items, grenades - will come to a stop inside instead, because friction
    /// keeps acting while their momentum does not; projectiles fly frictionless and are unaffected.
    /// The physics sleep threshold is 0.01 m/s, far below anything this produces, so nothing gets
    /// put to sleep and stranded mid-field.
    ///
    /// Networked because the shooter's own client slows its predicted copy of a round itself, and
    /// the two sides have to crawl at the same rate or the bullet the shooter watches and the
    /// bullet everyone else watches end up in different places.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public float TimeScale = 0.03f;

    /// <summary>
    /// Fraction of their walk and sprint speed a captured mob keeps. Deliberately far gentler than
    /// <see cref="TimeScale"/> - a field that pinned people in place would be a stunlock.
    /// </summary>
    [DataField]
    public float MobTimeScale = 0.25f;

    /// <summary>
    /// Speed multiplier applied to whatever the pulse throws back, relative to the speed the object
    /// was travelling at when it was caught.
    /// </summary>
    [DataField]
    public float PulseSpeedMultiplier = 1.35f;

    /// <summary>
    /// Floor speed for pulsed objects, so something that rolled in and stopped still gets launched.
    /// </summary>
    [DataField]
    public float PulseMinimumSpeed = 14f;

    /// <summary>
    /// How long the field stands before it unwinds on its own.
    /// </summary>
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Filled in at map init from <see cref="Lifetime"/>, so a field spawned by any route - the
    /// power, an admin, a map - expires instead of being culled on its first tick.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ExpiresAt;

    /// <summary>
    /// The Psion who cast this. Only they can collapse it with a pulse.
    /// </summary>
    [ViewVariables]
    public EntityUid Caster;

    /// <summary>
    /// Everything currently held by this field. Server-side bookkeeping: entries are mirrored by a
    /// <see cref="TemporallySlowedComponent"/> on each captured entity.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Captured = new();
}

/// <summary>
/// Marks an entity currently inside a <see cref="RecurrenceFieldComponent"/>.
/// </summary>
/// <remarks>
/// Networked because the movement penalty is applied in shared code, so the client can predict its
/// own crawl through the field instead of rubber-banding on every step.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class TemporallySlowedComponent : Component
{
    /// <summary>
    /// The field holding this entity. Cleared to <see cref="EntityUid.Invalid"/> once released.
    /// </summary>
    [ViewVariables]
    public EntityUid Field;

    /// <summary>
    /// The multiplier that was applied to this entity's velocity, remembered so release can undo
    /// exactly what capture did rather than guessing at an original speed.
    /// </summary>
    [ViewVariables]
    public float AppliedScale = 1f;

    /// <summary>
    /// Walk and sprint multiplier for mobs. Zero for objects, which are slowed through physics.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MovementScale = 1f;

    /// <summary>
    /// Velocity at the moment of capture, measured against the field rather than the map so a field
    /// cast aboard a moving ship does not read the ship's own speed as an incoming object. Used to
    /// aim the pulse back along the path the object came in on, and to give back the speed it had
    /// on the way in.
    /// </summary>
    [ViewVariables]
    public Vector2 EntryVelocity;

    /// <summary>
    /// Spin at the moment of capture, restored verbatim on release for the same reason as
    /// <see cref="EntryVelocity"/>.
    /// </summary>
    [ViewVariables]
    public float EntryAngularVelocity;
}
