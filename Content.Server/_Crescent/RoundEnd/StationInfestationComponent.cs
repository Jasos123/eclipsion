using Robust.Shared.Maths;

namespace Content.Server._Crescent.RoundEnd;

/// <summary>
/// Runtime state for a conquest station that has been claimed by the Turning. The grid remains in the world while
/// a deliberately small number of tiles rot away and aberrant flesh creatures emerge over time.
/// </summary>
[RegisterComponent, Access(typeof(StationInfestationSystem))]
public sealed partial class StationInfestationComponent : Component
{
    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public List<Vector2i> CandidateTiles = new();

    [ViewVariables]
    public HashSet<Vector2i> InfestedTiles = new();

    [ViewVariables]
    public HashSet<Vector2i> RemovedTiles = new();

    [ViewVariables]
    public HashSet<EntityUid> SpawnedMobs = new();

    [ViewVariables]
    public int Pulses;

    [ViewVariables]
    public int TotalMobsSpawned;
}
