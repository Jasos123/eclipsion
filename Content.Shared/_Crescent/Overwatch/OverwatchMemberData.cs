using System.Numerics;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Overwatch;

/// <summary>
/// Data about a faction member, for display in the Overwatch console.
/// </summary>
/// <param name="HasCamera">
/// Whether a camera feed can be opened for this member. False for the dead, whose feed shows nothing —
/// the server refuses those too, so this is not merely a UI hint.
/// </param>
/// <param name="Coordinates">
/// Position <b>local to <paramref name="LocationName"/>'s grid</b>, or null when the member is not on a
/// grid at all (in space, in nullspace, in cryo). Deliberately not world position: two members on
/// different maps produced similar-looking world coordinates that meant nothing next to each other.
/// </param>
/// <param name="LocationName">
/// Name of the grid the member is on, empty when they are not on one.
/// </param>
[Serializable, NetSerializable]
public sealed record OverwatchMemberData(
    NetEntity Member,
    string Name,
    string JobTitle,
    OverwatchMemberStatus Status,
    bool HasCamera,
    int? SquadId = null,
    string SquadName = "",
    Vector2? Coordinates = null,
    string LocationName = ""
);

/// <summary>
/// A faction member's status, for display in the Overwatch console.
/// </summary>
[Serializable, NetSerializable]
public enum OverwatchMemberStatus : byte
{
    Alive,
    Dead,
    SSD
}
