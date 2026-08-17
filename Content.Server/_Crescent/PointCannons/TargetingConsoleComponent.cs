using System.Numerics;
using Content.Shared.PointCannons;
using Robust.Shared.Timing;

namespace Content.Server.PointCannons;

[RegisterComponent]
public sealed partial class TargetingConsoleComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, List<EntityUid>> CannonGroups = new() { { "all", new() } };
    public HashSet<string> ActiveGroups = new();
    public string CurrentGroupName = string.Empty;
    //public List<EntityUid> CurrentGroup => CannonGroups[CurrentGroupName];
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public List<EntityUid> CurrentGroup = new();

    public bool RegenerateCannons = true;
    public TargetingConsoleBoundUserInterfaceState? PrevState;

    /// <summary>
    ///     Crescent - map point the standing fire order is aimed at, and when that order lapses.
    /// </summary>
    /// <remarks>
    ///     The console only sends a fire message ten times a second, but a gun's burst rate is measured against
    ///     the server tick. Firing straight off the message capped every cannon at ten rounds a second no matter
    ///     what its prototype said, so the order is held here and re-applied each tick instead. The expiry is
    ///     what keeps that safe: it is a couple of message intervals long, so a dropped packet does not stutter
    ///     the guns, and a client that stops sending - or vanishes mid-drag - can never leave them firing.
    /// </remarks>
    public Vector2 FireCoordinates;

    public TimeSpan FireOrderExpiry;
}