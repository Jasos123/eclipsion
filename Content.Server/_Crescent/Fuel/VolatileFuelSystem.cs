using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.IgnitionSource;
using Content.Shared.Atmos;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Fuel;

/// <summary>
/// Handles ignition and burning for spilled ship fuel.
/// </summary>
public sealed class VolatileFuelSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>Reagents handled by this system.</summary>
    public static readonly string[] VolatileReagents = { "BoriaticFuel", "AmeFuel" };

    private static readonly TimeSpan BurnCooldown = TimeSpan.FromSeconds(1);

    private const float BurnRate = 25f;

    private const float VapourPerUnit = 0.02f;

    private static readonly FixedPoint2 MinimumBurnableVolume = FixedPoint2.New(5);

    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    private readonly HashSet<(EntityUid Grid, Vector2i Indices)> _burningTiles = new();

    private readonly HashSet<(EntityUid Grid, Vector2i Indices)> _ignitionTiles = new();

    private TimeSpan _nextCycle;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextCycle)
            return;

        _nextCycle = _timing.CurTime + BurnCooldown;

        CollectIgnitionSources();
        BurnPuddles();
    }

    private void CollectIgnitionSources()
    {
        _ignitionTiles.Clear();

        // Carry existing fire to adjacent tiles.
        foreach (var tile in _burningTiles)
        {
            _ignitionTiles.Add(tile);
            foreach (var offset in Neighbours)
            {
                _ignitionTiles.Add((tile.Grid, tile.Indices + offset));
            }
        }

        _burningTiles.Clear();

        // Include any entity the game treats as an ignition source.
        var sources = EntityQueryEnumerator<IgnitionSourceComponent, TransformComponent>();
        while (sources.MoveNext(out var uid, out var source, out var xform))
        {
            if (source.Ignited)
                AddTileOf(uid, xform);
        }

        // Burning entities can ignite the tile beneath them.
        var burning = EntityQueryEnumerator<FlammableComponent, TransformComponent>();
        while (burning.MoveNext(out var uid, out var flammable, out var xform))
        {
            if (flammable.OnFire)
                AddTileOf(uid, xform);
        }
    }

    private void AddTileOf(EntityUid uid, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid)
            return;

        _ignitionTiles.Add((gridUid, _transform.GetGridOrMapTilePosition(uid, xform)));
    }

    private void BurnPuddles()
    {
        var burnAmount = FixedPoint2.New(BurnRate * (float) BurnCooldown.TotalSeconds);
        var query = EntityQueryEnumerator<PuddleComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var puddle, out var xform))
        {
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            if (!_solution.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var solution))
                continue;

            if (solution.GetTotalPrototypeQuantity(VolatileReagents) < MinimumBurnableVolume)
                continue;

            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (!_ignitionTiles.Contains((gridUid, indices)) && !_atmos.IsHotspotActive(gridUid, indices))
                continue;

            // Do not consume fuel when the atmosphere cannot burn it.
            var air = _atmos.GetTileMixture(gridUid, null, indices, true);
            if (air == null || air.GetMoles(Gas.Oxygen) < 0.5f)
                continue;

            var burned = solution.SplitSolutionWithOnly(burnAmount, VolatileReagents);
            if (burned.Volume <= FixedPoint2.Zero)
                continue;

            _solution.UpdateChemicals(puddle.Solution.Value);

            air.AdjustMoles(Gas.Plasma, burned.Volume.Float() * VapourPerUnit);
            _atmos.HotspotExpose(
                gridUid,
                indices,
                Atmospherics.PlasmaMinimumBurnTemperature * 2f,
                Atmospherics.CellVolume / 4f,
                uid,
                true);

            _burningTiles.Add((gridUid, indices));

            if (solution.Volume <= FixedPoint2.Zero)
                QueueDel(uid);
        }
    }
}
