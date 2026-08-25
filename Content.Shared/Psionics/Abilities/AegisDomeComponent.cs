using Robust.Shared.GameStates;

namespace Content.Shared.Abilities.Psionics;

/// <summary>
/// A standing barrier that shelters everyone under it rather than one person.
///
/// Unlike <see cref="PsionicEnergyShieldComponent"/>, which sits on the protected body and simply
/// runs out, the dome is a thing in the world with its own integrity: it eats a share of every hit
/// taken by anyone inside, and when it has eaten enough it shatters early.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class AegisDomeComponent : Component
{
    /// <summary>
    /// Radius in metres. Also drives how far the coverage scan reaches.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius = 2.5f;

    /// <summary>
    /// Fraction of incoming damage the dome takes on behalf of whoever is under it. The rest still
    /// lands - a dome is cover, not immunity.
    /// </summary>
    [DataField]
    public float Absorption = 0.6f;

    [DataField]
    public float MaxIntegrity = 250f;

    /// <summary>
    /// Damage the dome can still absorb. Networked so the client can shade the barrier by how close
    /// it is to failing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Integrity = 250f;

    /// <summary>
    /// How long the dome stands if nothing brings it down first.
    /// </summary>
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Filled in at map init from <see cref="Lifetime"/>, so a dome spawned by any route expires
    /// instead of being culled on its first tick.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ExpiresAt;

    /// <summary>
    /// The Psion who raised it, credited in logs and told when it fails.
    /// </summary>
    /// <remarks>
    /// Networked because the barrier only faces outwards: a shooter's own client has to be able to
    /// tell whether the round it is watching was fired from under the dome or at it, and the caster
    /// is inside by definition.
    /// </remarks>
    [AutoNetworkedField]
    public EntityUid Caster;

    /// <summary>
    /// Everyone currently under the dome. Mirrored by an <see cref="AegisShelteredComponent"/> on
    /// each of them.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Covered = new();

    /// <summary>
    /// Throttle so a burst of hits only flares the barrier once.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextImpact;
}

/// <summary>
/// Marks a mob standing under an <see cref="AegisDomeComponent"/>. Purely a back-reference: all the
/// damage handling lives on the dome.
/// </summary>
/// <remarks>
/// Networked for the same reason the dome's caster is: the shooter's client predicts its own rounds
/// and has to know whether it is standing inside the barrier they are crossing.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AegisShelteredComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Dome;
}
