using Content.Server.Worldgen.Components.Debris;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server.Worldgen.Systems.Debris;

/// <summary>
///     This handles populating simple structures, simply using a loot table for each tile.
/// </summary>
public sealed class SimpleFloorPlanPopulatorSystem : BaseWorldSystem
{
    [Dependency] private readonly SharedMapSystem _mapSystemCompat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinition = default!;

    /// <inheritdoc />
    public override void Initialize()
    {
        SubscribeLocalEvent<SimpleFloorPlanPopulatorComponent, LocalStructureLoadedEvent>(OnFloorPlanBuilt);
    }

    private void OnFloorPlanBuilt(EntityUid uid, SimpleFloorPlanPopulatorComponent component,
        LocalStructureLoadedEvent args)
    {
        var placeables = new List<string?>(4);
        var grid = Comp<MapGridComponent>(uid);
        var enumerator = _mapSystemCompat.GetAllTilesEnumerator(uid, grid);
        var tiles = new List<(Vector2i Indices, EntityCoordinates Coordinates, string Selector)>();

        while (enumerator.MoveNext(out var tile))
        {
            var coords = _mapSystemCompat.GridTileToLocal(uid, grid, tile.Value.GridIndices);
            var selector = tile.Value.Tile.GetContentTileDefinition(_tileDefinition).ID;
            tiles.Add((tile.Value.GridIndices, coords, selector));
        }

        var reservedTiles = new HashSet<Vector2i>();
        foreach (var (selector, guaranteedSpawns) in component.GuaranteedSpawns)
        {
            var candidates = tiles
                .Where(tile => tile.Selector == selector)
                .Select(tile => tile.Indices)
                .ToList();

            foreach (var proto in guaranteedSpawns)
            {
                if (candidates.Count == 0)
                    break;

                var indices = _random.PickAndTake(candidates);
                reservedTiles.Add(indices);
                var coords = _mapSystemCompat.GridTileToLocal(uid, grid, indices);
                Spawn(proto, coords);
            }
        }

        foreach (var (indices, coords, selector) in tiles)
        {
            if (reservedTiles.Contains(indices))
                continue;

            if (!component.Caches.TryGetValue(selector, out var cache))
                continue;

            placeables.Clear();
            cache.GetSpawns(_random, ref placeables);

            foreach (var proto in placeables)
            {
                if (proto is null)
                    continue;

                Spawn(proto, coords);
            }
        }
    }
}

