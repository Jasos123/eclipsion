using Content.Server.Maps;
using Content.Server.StationEvents.Events;
using Content.Shared.Storage;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents.Components;

[RegisterComponent, Access(typeof(BluespaceErrorRule))]
public sealed partial class BluespaceErrorRuleComponent : Component
{
    /// <summary>
    /// Path to the grid that gets bluspaced in
    /// </summary>
    [DataField("gridPath")]
    public string GridPath = "";

    /// <summary>
    /// Game map whose station config the spawned grid is initialized with. Set this if the grid needs to be a
    /// station in its own right, e.g. so that cargo consoles on it resolve an order database and a bank account.
    /// Leave unset for plain loot grids. Falls back to a game map named after the grid file.
    /// </summary>
    [DataField("stationMap")]
    public ProtoId<GameMapPrototype>? StationMap;

    /// <summary>
    /// The station spun up for the grid, if <see cref="StationMap"/> produced one. Set after starting the event,
    /// and torn down with the grid so the round isn't left holding gridless stations.
    /// </summary>
    [DataField("stationUid")]
    public EntityUid? StationUid = null;

    /// <summary>
    /// The color of your thing. the name should be set by the mapper when mapping.
    /// </summary>
    [DataField("color")]
    public Color Color = new Color(225, 15, 155);

    /// <summary>
    /// Multiplier to apply to the remaining value of a grid, to be deposited in the station account for defending
    /// </summary>
    [DataField("rewardFactor")]
    public float RewardFactor = 0f;

    /// <summary>
    /// The grid in question, set after starting the event
    /// </summary>
    [DataField("gridUid")]
    public EntityUid? GridUid = null;

    /// <summary>
    /// How much the grid is appraised at upon entering into existance, set after starting the event
    /// </summary>
    [DataField("startingValue")]
    public double startingValue = 0;

    /// <summary>
    /// the minimum x value for the grid to spawn at
    /// </summary>
    [DataField("minX")]
    public float minX = -10000f;

    /// <summary>
    /// the minimum y value for the grid to spawn at
    /// </summary>
    [DataField("minY")]
    public float minY = -10000f;

    /// <summary>
    /// the maximum x value for the grid to spawn at
    /// </summary>
    [DataField("maxX")]
    public float maxX = 10000f;

    /// <summary>
    /// the maximum x value for the grid to spawn at
    /// </summary>
    [DataField("maxY")]
    public float maxY = 10000f;

    /// <summary>
    /// Inner radius of the ring around the belt centre the grid spawns in. Set this together with
    /// <see cref="MaxDistance"/> to spawn at a distance instead of anywhere inside the min/max box - the box is a
    /// rectangle that happily contains the belt itself, so it cannot keep a grid away from the middle of the map.
    /// </summary>
    [DataField("minDistance")]
    public float? MinDistance;

    /// <summary>
    /// Outer radius of the ring around the belt centre the grid spawns in. See <see cref="MinDistance"/>.
    /// </summary>
    [DataField("maxDistance")]
    public float? MaxDistance;
}
