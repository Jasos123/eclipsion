using Robust.Shared.Map.Components;

namespace Content.Server._Crescent.ShipAI;

/// <summary>
///     Keeps <see cref="ShipHullMonitorComponent.Integrity"/> up to date for ship NPCs.
/// </summary>
public sealed class ShipHullMonitorSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<ShipHullMonitorComponent, MapInitEvent>(OnMapInit);
    }

    /// <summary>
    ///     Takes the baseline the moment the ship exists instead of waiting out the first interval. The
    ///     baseline is whatever the first sample reads, so a ship shot apart during that window would have
    ///     had the damage folded straight into its own baseline and gone on reporting a pristine hull.
    /// </summary>
    private void OnMapInit(Entity<ShipHullMonitorComponent> ent, ref MapInitEvent args)
    {
        Sample(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipHullMonitorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.Accumulator += frameTime;
            if (comp.Accumulator < comp.UpdateInterval)
                continue;

            comp.Accumulator = 0f;
            Sample(uid, comp);
        }
    }

    private void Sample(EntityUid uid, ShipHullMonitorComponent comp)
    {
        // No grid means the hull is already gone from under us; leave the last reading alone rather than
        // reporting a pristine ship. At map init it can also mean the grid is still being built, which the
        // running-maximum baseline below absorbs on the next sample.
        if (Transform(uid).GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
            return;

        var count = 0;
        var tiles = _map.GetAllTilesEnumerator(gridUid, grid);
        while (tiles.MoveNext(out _))
            count++;

        if (count > comp.BaselineTileCount)
            comp.BaselineTileCount = count;

        comp.Integrity = comp.BaselineTileCount > 0
            ? Math.Clamp(count / (float) comp.BaselineTileCount, 0f, 1f)
            : 1f;
    }

    /// <summary>
    ///     Remaining hull fraction for <paramref name="uid"/>. An entity nothing is monitoring reads as
    ///     undamaged, so a behaviour written against this never fires on a ship that simply has no monitor.
    /// </summary>
    public float GetIntegrity(EntityUid uid, ShipHullMonitorComponent? comp = null)
    {
        return Resolve(uid, ref comp, false) ? comp.Integrity : 1f;
    }
}
