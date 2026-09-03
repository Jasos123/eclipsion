using Content.Server.Atmos.Components;
using Content.Shared._Crescent.RepairStation;
using Content.Shared.Atmos;
using Robust.Shared.Map.Components;

namespace Content.Server._Crescent.RepairStation;

public sealed partial class ShipRepairStationSystem
{
    /// <summary>
    /// The mixes the yard puts back into a hull, indexed the same way the atmos fix marker numbers them so
    /// a mapper's cold room or vox berth comes back as what it was rather than as ordinary air.
    /// </summary>
    private static readonly GasMixture[] StandardMixtures = BuildStandardMixtures();

    private static GasMixture[] BuildStandardMixtures()
    {
        var mixtures = new GasMixture[8];
        for (var i = 0; i < mixtures.Length; i++)
            mixtures[i] = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };

        // 0: air
        mixtures[0].AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        mixtures[0].AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

        // 1: vacuum

        // 2: oxygen
        mixtures[2].AdjustMoles(Gas.Oxygen, Atmospherics.MolesCellGasMiner);

        // 3: nitrogen
        mixtures[3].AdjustMoles(Gas.Nitrogen, Atmospherics.MolesCellGasMiner);

        // 4: plasma
        mixtures[4].AdjustMoles(Gas.Plasma, Atmospherics.MolesCellGasMiner);

        // 5: plasmafire
        mixtures[5].AdjustMoles(Gas.Oxygen, Atmospherics.MolesCellGasMiner);
        mixtures[5].AdjustMoles(Gas.Plasma, Atmospherics.MolesCellGasMiner);
        mixtures[5].Temperature = 5000f;

        // 6: freezer
        mixtures[6].AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        mixtures[6].AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        mixtures[6].Temperature = 235f;

        // 7: nitrogen at one atmosphere, for vox berths
        mixtures[7].AdjustMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard);

        return mixtures;
    }

    /// <summary>
    /// The four ways air spreads out of a tile, and the direction the neighbour has to be open in to take it.
    /// </summary>
    private static readonly (Vector2i Offset, AtmosDirection Dir)[] Cardinals =
    {
        (new Vector2i(0, 1), AtmosDirection.North),
        (new Vector2i(0, -1), AtmosDirection.South),
        (new Vector2i(1, 0), AtmosDirection.East),
        (new Vector2i(-1, 0), AtmosDirection.West),
    };

    /// <summary>
    /// Ceiling on how far one job's filling reaches, so a hull whose compartments are all open to each other
    /// cannot turn a single welded breach into a pass over the whole ship.
    /// </summary>
    private const int MaxRefillTiles = 4096;

    /// <summary>
    /// Books the compartments the yard has just sealed in for a lungful of air.
    /// </summary>
    /// <remarks>
    /// Not done on the spot. Freshly laid plating only joins the grid's atmosphere on the atmos system's
    /// next revalidation pass, so filling the moment the last tile goes down would breathe air into every
    /// compartment except the ones just rebuilt.
    /// </remarks>
    private void ScheduleAtmosRefresh(Entity<ShipRepairStationComponent> station, EntityUid ship)
    {
        // Nothing was sealed, so nothing was vented by the damage either. A job that only swept the deck
        // or topped up a magazine has no business touching the air the crew is standing in.
        if (station.Comp.RefillSeeds.Count == 0)
            return;

        station.Comp.AtmosTarget = ship;
        station.Comp.AtmosSeeds = station.Comp.RefillSeeds;
        station.Comp.RefillSeeds = new HashSet<Vector2i>();
        station.Comp.AtmosRefreshTime = _timing.CurTime + TimeSpan.FromSeconds(station.Comp.AtmosSettleSeconds);
    }

    /// <summary>
    /// Makes the air good again behind the holes the yard has just welded shut. A compartment open to space
    /// is a compartment emptied of air, and sealing it without filling what is behind it hands the customer
    /// back an airtight tomb.
    /// </summary>
    /// <remarks>
    /// Filling spreads out of each sealed hole and stops at the first thing that holds air back, so it
    /// reaches exactly the rooms the damage emptied. Everything on the other side of a wall or a shut door
    /// is left as the crew has it - a fire they are fighting, a hold vented on purpose, a plasma leak they
    /// have sealed off - none of which is the slip's to undo. A room still holding a breathable pressure
    /// is passed over on the same grounds, so putting an internal wall back does not blow the air in the
    /// rooms either side of it away and start them over.
    /// </remarks>
    private void RefreshAtmosphere(EntityUid ship, HashSet<Vector2i> breaches)
    {
        if (TerminatingOrDeleted(ship)
            || !TryComp<MapGridComponent>(ship, out var gridComp)
            || !_atmos.HasAtmosphere(ship))
        {
            return;
        }

        var markers = GetEntityQuery<AtmosFixMarkerComponent>();
        var queued = new HashSet<Vector2i>(breaches);
        var pending = new Queue<Vector2i>(breaches);
        var filled = 0;

        while (pending.TryDequeue(out var indices) && filled < MaxRefillTiles)
        {
            // Space, or a tile the grid's atmosphere does not hold. Nothing to fill and nothing behind it,
            // so the fill stops rather than spilling out through the hull. A tile that holds air but did
            // not need any still passes the air on to whatever is behind it.
            if (!TryRefillTile(ship, gridComp, indices, markers))
                continue;

            filled++;

            foreach (var (offset, dir) in Cardinals)
            {
                var neighbour = indices + offset;
                if (queued.Contains(neighbour))
                    continue;

                // A wall, a shut airlock or a closed firelock on either side of the edge keeps the air in.
                if (_atmos.IsTileAirBlocked(ship, indices, dir, gridComp)
                    || _atmos.IsTileAirBlocked(ship, neighbour, dir.GetOpposite(), gridComp))
                {
                    continue;
                }

                queued.Add(neighbour);
                pending.Enqueue(neighbour);
            }
        }
    }

    /// <summary>
    /// Puts a room's worth of air on one tile that wants it. False only when there is nothing there at
    /// all, which is where the filling stops; a tile that is already habitable is left untouched but
    /// still carries the fill on to its neighbours.
    /// </summary>
    private bool TryRefillTile(
        EntityUid ship,
        MapGridComponent gridComp,
        Vector2i indices,
        EntityQuery<AtmosFixMarkerComponent> markers)
    {
        if (_map.GetTileRef(ship, gridComp, indices).Tile.IsEmpty)
            return false;

        var air = _atmos.GetTileMixture(ship, null, indices, true);
        if (air is not { Immutable: false })
            return false;

        // Thin enough that a crewman would be warned about it is the line between a compartment the
        // damage emptied and one the crew is living in. Above it the yard has nothing to make good.
        if (air.Pressure >= Atmospherics.WarningLowPressure)
            return true;

        // A marker on the tile is the mapper saying what belongs in this room.
        var mixture = StandardMixtures[0];
        GasMixture? custom = null;
        var anchored = _map.GetAnchoredEntitiesEnumerator(ship, gridComp, indices);
        while (anchored.MoveNext(out var anchoredUid))
        {
            if (!markers.TryComp(anchoredUid, out var marker))
                continue;

            if (marker.Mode >= 0 && marker.Mode < StandardMixtures.Length)
                mixture = StandardMixtures[marker.Mode];

            custom = marker.GasMix;
        }

        air.Clear();
        _atmos.Merge(air, custom ?? mixture);
        air.Temperature = mixture.Temperature;
        return true;
    }
}
