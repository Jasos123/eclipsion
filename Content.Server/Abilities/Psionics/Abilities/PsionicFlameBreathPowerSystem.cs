using System.Numerics;
using Content.Server._Crescent.Barricades;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Abilities.Psionics;

/// <summary>
/// Projects a four-tile, wall-bounded cone of persistent Crescent/RMC floor fire.
/// </summary>
public sealed class PsionicFlameBreathPowerSystem : EntitySystem
{
    private const float ConeLength = 4.5f;
    private const float ConeHalfAngleTangent = 0.5f;

    [Dependency] private readonly CrescentTileFireSystem _tileFire = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PsionicFlameBreathActionEvent>(OnFlameBreath);
    }

    private void OnFlameBreath(PsionicFlameBreathActionEvent args)
    {
        if (args.Handled
            || !_psionics.OnAttemptPowerUse(args.Performer, "flame breath", true)
            || !TryComp<MapGridComponent>(Transform(args.Performer).GridUid, out var grid))
            return;

        var gridUid = Transform(args.Performer).GridUid!.Value;
        var originMap = _transform.GetMapCoordinates(args.Performer);
        var targetMap = _transform.ToMapCoordinates(args.Target);
        if (originMap.MapId != targetMap.MapId)
            return;

        var origin = _map.MapToGrid(gridUid, originMap);
        var target = _map.MapToGrid(gridUid, targetMap);
        var direction = target.Position - origin.Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        direction = Vector2.Normalize(direction);
        var originTile = _map.TileIndicesFor(gridUid, grid, origin);
        var extent = (int) MathF.Ceiling(ConeLength);

        for (var x = -extent; x <= extent; x++)
            for (var y = -extent; y <= extent; y++)
            {
                var candidate = originTile + new Vector2i(x, y);
                var coordinates = _map.GridTileToLocal(gridUid, grid, candidate);
                var delta = coordinates.Position - origin.Position;
                var forward = Vector2.Dot(delta, direction);
                var lateral = MathF.Abs(delta.X * direction.Y - delta.Y * direction.X);

                if (forward < 0.35f
                    || forward > ConeLength
                    || lateral > MathF.Max(0.45f, forward * ConeHalfAngleTangent)
                    || IsOccluded(gridUid, grid, originTile, candidate))
                    continue;

                _tileFire.TrySpawnTileFire(gridUid, grid, candidate);
            }

        var visual = Spawn("EffectPsionicFlameBreath", Transform(args.Performer).Coordinates);
        _transform.SetParent(visual, args.Performer);
        _transform.SetLocalPosition(visual, direction * 0.55f);
        _transform.SetLocalRotation(visual, direction.ToWorldAngle());

        _psionics.LogPowerUsed(args.Performer, "flame breath", 5, 8);
        args.Handled = true;
    }

    private bool IsOccluded(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i origin,
        Vector2i destination)
    {
        var delta = destination - origin;
        var steps = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
        for (var step = 1; step < steps; step++)
        {
            var progress = (float) step / steps;
            var indices = new Vector2i(
                (int) MathF.Round(origin.X + delta.X * progress),
                (int) MathF.Round(origin.Y + delta.Y * progress));

            if (_tileFire.IsFireBlocked(gridUid, grid, indices))
                return true;
        }

        return false;
    }
}
