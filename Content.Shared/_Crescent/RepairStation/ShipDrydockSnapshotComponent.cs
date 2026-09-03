using System.Numerics;
using Content.Shared.Decals;
using Robust.Shared.Prototypes;

namespace Content.Shared._Crescent.RepairStation;

/// <summary>
/// The repair slip's own file of a hull, covering everything the scope prototypes take that the
/// hand-held repair device does not already track.
/// </summary>
/// <remarks>
/// Server-side only on purpose. It is neither networked nor saved with the map, so widening the
/// drydock's reach costs no bandwidth and leaves the device's snapshot - which every client does
/// receive - exactly the size it was.
/// </remarks>
[RegisterComponent]
public sealed partial class ShipDrydockSnapshotComponent : Component
{
    [ViewVariables]
    public List<DrydockPart> Parts = new();

    /// <summary>
    /// The deck markings the hull wore when it was last surveyed. Painted stripes, hazard blocks and
    /// the like go with the plating they were laid on, and the slip lays them back down with it.
    /// </summary>
    /// <remarks>
    /// Only markings that stay put are on file: nothing cleanable, since blood and soot are mess
    /// rather than livery, and nothing the directional tiling system draws, since that redraws itself
    /// the moment the plating is back.
    /// </remarks>
    [ViewVariables]
    public List<Decal> Decals = new();
}

/// <summary>
/// One structure as it stood when the hull was last surveyed.
/// </summary>
public sealed class DrydockPart
{
    public EntProtoId Proto;

    /// <summary>
    /// Tile the structure sat on, used to check whether something already stands in its place.
    /// </summary>
    public Vector2i Tile;

    public Vector2 LocalPosition;
    public Angle Rotation;

    /// <summary>
    /// The structure itself, while it lasts. Re-pointed at the replacement when the slip rebuilds it.
    /// </summary>
    public EntityUid? Original;

    /// <summary>
    /// Whether this is a weapon hardpoint. A gun only hunts for its mount as it spawns, so mounts are
    /// rebuilt before anything else that stands on the same tile.
    /// </summary>
    public bool IsMount;
}
