using System.Linq;
using Content.Shared._Crescent.RepairStation;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Decals;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.RepairStation;

/// <summary>
/// Keeps the repair slip's own file of a hull. It rides along with the hand-held device's snapshot -
/// same moments, same grid - but records everything the <see cref="ShipRepairScopePrototype"/> files
/// allow rather than only what the device can weld.
/// </summary>
public sealed class ShipDrydockSnapshotSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private readonly List<ShipRepairScopePrototype> _scopes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MapGridComponent, ShipSnapshotGeneratedEvent>(OnSnapshotGenerated);

        _proto.PrototypesReloaded += _ => CacheScopes();
        CacheScopes();
    }

    private void CacheScopes()
    {
        _scopes.Clear();
        _scopes.AddRange(_proto.EnumeratePrototypes<ShipRepairScopePrototype>().OrderBy(s => s.ID));
    }

    private void OnSnapshotGenerated(Entity<MapGridComponent> grid, ref ShipSnapshotGeneratedEvent args)
    {
        Capture(grid);
    }

    /// <summary>
    /// Records every anchored structure on the hull that any scope takes.
    /// </summary>
    public void Capture(Entity<MapGridComponent> grid)
    {
        if (_scopes.Count == 0)
        {
            RemCompDeferred<ShipDrydockSnapshotComponent>(grid);
            return;
        }

        var snapshot = EnsureComp<ShipDrydockSnapshotComponent>(grid);
        snapshot.Parts.Clear();

        CaptureDecals(grid, snapshot);

        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TerminatingOrDeleted(child))
                continue;

            // Walls and the like are already on the hand device's file. Recording them twice would
            // have the slip bill for them twice and stack two of them on the tile.
            if (HasComp<ShipRepairableComponent>(child))
                continue;

            var xform = Transform(child);
            if (!xform.Anchored)
                continue;

            if (MetaData(child).EntityPrototype is not { } proto)
                continue;

            if (!InScope(child))
                continue;

            snapshot.Parts.Add(new DrydockPart
            {
                Proto = proto.ID,
                Tile = _map.LocalToTile(grid, grid.Comp, xform.Coordinates),
                LocalPosition = xform.LocalPosition,
                Rotation = xform.LocalRotation,
                Original = child,
                IsMount = HasComp<HardpointComponent>(child),
            });
        }
    }

    /// <summary>
    /// Records the markings painted on the deck. A decal is not an entity and has no snapshot of its
    /// own, so without this a rebuilt compartment comes back as bare plating.
    /// </summary>
    private void CaptureDecals(EntityUid grid, ShipDrydockSnapshotComponent snapshot)
    {
        snapshot.Decals.Clear();

        if (!TryComp<DecalGridComponent>(grid, out var decals))
            return;

        foreach (var (_, chunk) in decals.ChunkCollection.ChunkCollection)
        {
            foreach (var (_, decal) in chunk.Decals)
            {
                // Blood, soot and the rest are mess the crew mops up, not livery the yard owes them,
                // and the tiling system redraws its own edging as soon as the plating is back.
                if (decal.Cleanable || decal.Directional)
                    continue;

                snapshot.Decals.Add(decal);
            }
        }
    }

    /// <summary>
    /// Whether any scope calls this wreckage the slip may clear off a tile it is rebuilding.
    /// </summary>
    public bool IsClearable(EntityUid uid)
    {
        foreach (var scope in _scopes)
        {
            if (scope.Clear != null && _whitelist.IsWhitelistPass(scope.Clear, uid))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether any scope calls this loose wreckage the slip sweeps up. Only ever asked of something
    /// lying unanchored on the deck.
    /// </summary>
    public bool IsDebris(EntityUid uid)
    {
        foreach (var scope in _scopes)
        {
            if (scope.Debris != null && _whitelist.IsWhitelistPass(scope.Debris, uid))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A structure is on file if any one scope both accepts and does not refuse it.
    /// </summary>
    private bool InScope(EntityUid uid)
    {
        foreach (var scope in _scopes)
        {
            if (_whitelist.IsWhitelistFail(scope.Whitelist, uid))
                continue;

            if (_whitelist.IsBlacklistPass(scope.Blacklist, uid))
                continue;

            return true;
        }

        return false;
    }
}
