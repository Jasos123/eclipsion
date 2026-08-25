namespace Content.Shared._Crescent.Corpses;

/// <summary>
/// Makes a dead body behave like dead weight: it slows to a stop instead of sailing off, and it can
/// barely be thrown. Added to a mob by <c>CorpsePhysicsSystem</c> when it dies and stripped again if
/// it is ever revived, so the values below are only ever live on an actual corpse.
/// </summary>
/// <remarks>
/// Bodies used to drift forever. Tile friction is skipped entirely for weightless entities and for
/// anything still in the air from a throw, so a corpse shoved in a corridor - or blown out of one by a
/// shipgun - kept its velocity until it hit something. Physics damping is applied by the solver itself
/// on both client and server, which is why the fix lives here rather than in a per-tick velocity clamp.
///
/// Authoring this component on a mob prototype is optional and only changes the numbers; the system
/// applies its own defaults to every mob that dies without one.
/// </remarks>
[RegisterComponent]
public sealed partial class CorpseWeightComponent : Component
{
    /// <summary>
    /// Linear damping applied to the corpse. Roughly "velocity decays to nothing over 1/value seconds",
    /// and unlike tile friction it works in zero gravity, which is the case people actually complained about.
    /// </summary>
    [DataField]
    public float LinearDamping = 2.5f;

    /// <summary>Angular damping applied to the corpse, so a spinning body settles instead of twirling forever.</summary>
    [DataField]
    public float AngularDamping = 2.5f;

    /// <summary>
    /// Extra tile friction multiplier on top of the surface's own, so a corpse skidding across a floor
    /// stops in a body length or two rather than crossing the room.
    /// </summary>
    [DataField]
    public float FrictionModifier = 3f;

    /// <summary>
    /// How much of a throw a corpse actually takes. Throwing impulse divides out by mass, so making the
    /// body heavier does nothing on its own - the launch speed has to be cut directly.
    /// </summary>
    [DataField]
    public float ThrowSpeedMultiplier = 0.3f;

    // --- Runtime state (not authored in YAML) -----------------------------

    /// <summary>Whether the damping below is currently installed on the body.</summary>
    [ViewVariables]
    public bool Applied;

    /// <summary>The body's own linear damping, saved so a revived mob gets its physics back untouched.</summary>
    [ViewVariables]
    public float OriginalLinearDamping;

    /// <summary>The body's own angular damping, saved for the same reason.</summary>
    [ViewVariables]
    public float OriginalAngularDamping;

    /// <summary>Whether the mob already had a tile friction modifier of its own before it died.</summary>
    [ViewVariables]
    public bool HadFrictionModifier;

    /// <summary>The mob's own tile friction modifier, saved so reviving restores it.</summary>
    [ViewVariables]
    public float OriginalFrictionModifier;
}
