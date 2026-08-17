namespace Content.Server._Crescent.ShipAI;

/// <summary>
///     Tracks how much of its grid a ship NPC has left, so its behaviour tree can decide to break off a fight
///     it is losing. Sits on the NPC entity itself (the AI core / control server), not on the grid, because
///     that is what an <see cref="Content.Server.NPC.HTN.Preconditions.HTNPrecondition"/> can reach.
/// </summary>
[RegisterComponent]
public sealed partial class ShipHullMonitorComponent : Component
{
    /// <summary>
    ///     The largest tile count we have ever measured, used as the "undamaged" baseline. Tracked as a
    ///     running maximum rather than sampled once at init: a grid that a spawner is still streaming in has
    ///     fewer tiles than it will end up with, and taking that as the baseline would leave the ship
    ///     permanently reading as over-full. Repairs raise it back the same way.
    /// </summary>
    [ViewVariables]
    public int BaselineTileCount;

    /// <summary>
    ///     Remaining fraction of <see cref="BaselineTileCount"/>. Stays 1 until the first sample lands, so a
    ///     ship never reads as damaged just because nothing has measured it yet.
    /// </summary>
    [ViewVariables]
    public float Integrity = 1f;

    /// <summary>
    ///     Seconds between samples. Measuring walks every tile of the grid, so this is deliberately slow -
    ///     losing a hull is not something that has to be noticed within a tick.
    /// </summary>
    [DataField]
    public float UpdateInterval = 5f;

    [ViewVariables]
    public float Accumulator;
}
