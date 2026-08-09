using Content.Shared.Whitelist;

namespace Content.Shared._Crescent.World;

/// <summary>
/// Deletes a worldgen asteroid once players have hollowed it out and walked away from it.
/// </summary>
/// <remarks>
/// The baseline is taken the moment worldgen populates the rock, which only happens once a player gets close enough
/// to load it. An asteroid nobody ever visited therefore never has a baseline and never decays.
/// </remarks>
[RegisterComponent]
public sealed partial class MinedAsteroidDecayComponent : Component
{
    /// <summary>
    /// Fraction of the original rock that has to be gone before the clock starts.
    /// </summary>
    [DataField]
    public float DepletionThreshold = 0.5f;

    /// <summary>
    /// How long a hollowed-out asteroid sticks around once it crosses <see cref="DepletionThreshold"/>.
    /// </summary>
    [DataField]
    public TimeSpan DecayDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How often the remaining rock is recounted.
    /// </summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Anything on the grid matching this claims the asteroid and stops it from ever decaying: generators, seep
    /// drills, whatever else a crew bolts down. Nothing is claimed if this is unset.
    /// </summary>
    [DataField]
    public EntityWhitelist? ClaimWhitelist;

    /// <summary>
    /// A player this close to the rock postpones deletion, so grids never vanish in front of anyone.
    /// </summary>
    [DataField]
    public float PlayerSafeRange = 128f;

    /// <summary>
    /// Rock the asteroid had when worldgen finished populating it. Zero until that happens.
    /// </summary>
    [ViewVariables]
    public int InitialRock;

    /// <summary>
    /// Whether the baseline has already been taken. Guards against a second populate pass re-baselining a rock that
    /// is already half mined.
    /// </summary>
    [ViewVariables]
    public bool Baselined;

    /// <summary>
    /// When the asteroid gets deleted, or null while it is still worth mining or still claimed.
    /// </summary>
    [ViewVariables]
    public TimeSpan? DecayAt;

    /// <summary>
    /// Next recount, staggered so belts do not survey themselves all on the same tick.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextCheck;
}
