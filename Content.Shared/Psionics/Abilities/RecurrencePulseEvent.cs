using Robust.Shared.Serialization;

namespace Content.Shared.Abilities.Psionics;

/// <summary>
/// Announces that a recurrence field has been collapsed by a pulse, so clients can turn the rounds
/// they are holding around too.
/// </summary>
/// <remarks>
/// The collapse itself is server business, and for every bullet but one that is enough. The
/// exception is the round the local player fired: with <c>rmc.gun_prediction</c> on, the shooter is
/// watching a client-side copy of it and the server's own round is hidden from them, so a pulse that
/// only turns the server's round around leaves the shooter watching their bullet carry on to the
/// target as though nothing had happened.
///
/// The field is deleted in the same breath as this is raised and the launch numbers are read off it,
/// so they travel in the message rather than being looked up from an entity that may already be
/// gone by the time this lands.
/// </remarks>
[Serializable, NetSerializable]
public sealed class RecurrencePulseEvent : EntityEventArgs
{
    /// <summary>
    /// The field that collapsed. Held rounds are matched against this rather than against a local
    /// entity id, which the deletion may already have taken away.
    /// </summary>
    public NetEntity Field;

    /// <summary>
    /// Speed multiplier applied to whatever the pulse throws back, relative to the speed the object
    /// was travelling at when it was caught.
    /// </summary>
    public float SpeedMultiplier;

    /// <summary>
    /// Floor speed for pulsed objects.
    /// </summary>
    public float MinimumSpeed;

    /// <summary>
    /// The Psion who collapsed the field. A returned round belongs to them now, so the client has to
    /// re-attribute its copy the same way the server does - otherwise the round passes harmlessly
    /// through the person who fired it on their own screen while the server counts it as a hit.
    /// </summary>
    public NetEntity Caster;

    public RecurrencePulseEvent(NetEntity field, NetEntity caster, float speedMultiplier, float minimumSpeed)
    {
        Field = field;
        Caster = caster;
        SpeedMultiplier = speedMultiplier;
        MinimumSpeed = minimumSpeed;
    }
}
