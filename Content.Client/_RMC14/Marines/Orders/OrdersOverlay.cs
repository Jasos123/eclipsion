using System.Numerics;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._RMC14.Marines.Orders;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.Marines.Orders;

/// <summary>
/// Draws active order icons above allied entities.
/// </summary>
public sealed class OrdersOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;

    private readonly ShaderInstance _shader;

    // The glyphs share the same corner of their canvases, so stacked orders need a small offset.
    private const float IconSpacing = 0.35f;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public OrdersOverlay()
    {
        IoCManager.InjectDependencies(this);

        _sprite = _entity.System<SpriteSystem>();
        _transform = _entity.System<TransformSystem>();

        _shader = _prototype.Index<ShaderPrototype>("unshaded").Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Only show orders issued to the local player's faction.
        if (!_entity.TryGetComponent(_players.LocalEntity, out HullrotFactionComponent? faction) ||
            string.IsNullOrEmpty(faction.Faction))
        {
            return;
        }

        var handle = args.WorldHandle;
        var eyeRot = args.Viewport.Eye?.Rotation ?? default;

        var scaleMatrix = Matrix3Helpers.CreateScale(new Vector2(1, 1));
        var rotationMatrix = Matrix3Helpers.CreateRotation(-eyeRot);

        handle.UseShader(_shader);

        var moveOrders = _entity.AllEntityQueryEnumerator<MoveOrderComponent, SpriteComponent, TransformComponent>();
        while (moveOrders.MoveNext(out var uid, out var move, out var sprite, out var xform))
        {
            DrawIcon((uid, sprite, xform), in args, faction.Faction, move.Icon, 0, scaleMatrix, rotationMatrix);
        }

        var holdOrders = _entity.AllEntityQueryEnumerator<HoldOrderComponent, SpriteComponent, TransformComponent>();
        while (holdOrders.MoveNext(out var uid, out var hold, out var sprite, out var xform))
        {
            DrawIcon((uid, sprite, xform), in args, faction.Faction, hold.Icon, 1, scaleMatrix, rotationMatrix);
        }

        var focusOrders = _entity.AllEntityQueryEnumerator<FocusOrderComponent, SpriteComponent, TransformComponent>();
        while (focusOrders.MoveNext(out var uid, out var focus, out var sprite, out var xform))
        {
            DrawIcon((uid, sprite, xform), in args, faction.Faction, focus.Icon, 2, scaleMatrix, rotationMatrix);
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private void DrawIcon(
        Entity<SpriteComponent, TransformComponent> ent,
        in OverlayDrawArgs args,
        string viewerFaction,
        SpriteSpecifier icon,
        int slot,
        Matrix3x2 scaleMatrix,
        Matrix3x2 rotationMatrix)
    {
        var (uid, sprite, xform) = ent;
        if (xform.MapID != args.MapId)
            return;

        if (!_entity.TryGetComponent(uid, out HullrotFactionComponent? faction) ||
            faction.Faction != viewerFaction)
        {
            return;
        }

        var bounds = sprite.Bounds;

        var worldPos = _transform.GetWorldPosition(xform);

        if (!bounds.Translated(worldPos).Intersects(args.WorldAABB))
            return;

        var handle = args.WorldHandle;
        var worldMatrix = Matrix3x2.CreateTranslation(worldPos);
        var scaledWorld = Matrix3x2.Multiply(scaleMatrix, worldMatrix);
        var matrix = Matrix3x2.Multiply(rotationMatrix, scaledWorld);
        handle.SetTransform(matrix);

        var texture = _sprite.GetFrame(icon, _timing.CurTime);

        var yOffset = (bounds.Height + sprite.Offset.Y) / 2f - (float) texture.Height / EyeManager.PixelsPerMeter;
        var xOffset = (bounds.Width + sprite.Offset.X) / 2f - (float) texture.Width / EyeManager.PixelsPerMeter - 0.25f + slot * IconSpacing;

        var position = new Vector2(xOffset, yOffset);
        handle.DrawTexture(texture, position);
    }
}
