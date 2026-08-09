using System.Numerics;
using Content.Client.Items.Systems;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.UserInterface.Systems.Storage.Controls;

public sealed class ItemGridPiece : Control, IEntityControl
{
    private readonly IEntityManager _entityManager;
    private readonly StorageUIController _storageController;

    private readonly List<(Texture, Vector2)> _texturesPositions = new();

    public readonly EntityUid Entity;
    public ItemStorageLocation Location;
    public ItemGridPieceMarks? Marked;

    /// <summary>
    /// When set, <see cref="Draw"/> only paints the backing tiles and the owning
    /// <see cref="StorageWindow"/> paints the icon afterwards via <see cref="DrawIcon"/>.
    /// The backing tiles are fully opaque and item icons are drawn larger than the shape they
    /// occupy, so without that second pass an icon gets painted over by the tiles of whichever
    /// item sits in a later grid cell. Cleared while the piece is dragged around, because it is
    /// then drawn standalone on the popup root.
    /// </summary>
    public bool DeferIconDraw;

    /// <summary>
    /// Top-left corner of the marker badge, in this control's own pixels. Set by the tile pass and
    /// consumed by the icon pass so the badge stays on top of the icon.
    /// </summary>
    private Vector2? _markedPosition;

    public event Action<GUIBoundKeyEventArgs, ItemGridPiece>? OnPiecePressed;
    public event Action<GUIBoundKeyEventArgs, ItemGridPiece>? OnPieceUnpressed;

    #region Textures
    private readonly string _centerTexturePath = "Storage/piece_center";
    private Texture? _centerTexture;
    private readonly string _topTexturePath = "Storage/piece_top";
    private Texture? _topTexture;
    private readonly string _bottomTexturePath = "Storage/piece_bottom";
    private Texture? _bottomTexture;
    private readonly string _leftTexturePath = "Storage/piece_left";
    private Texture? _leftTexture;
    private readonly string _rightTexturePath = "Storage/piece_right";
    private Texture? _rightTexture;
    private readonly string _topLeftTexturePath = "Storage/piece_topLeft";
    private Texture? _topLeftTexture;
    private readonly string _topRightTexturePath = "Storage/piece_topRight";
    private Texture? _topRightTexture;
    private readonly string _bottomLeftTexturePath = "Storage/piece_bottomLeft";
    private Texture? _bottomLeftTexture;
    private readonly string _bottomRightTexturePath = "Storage/piece_bottomRight";
    private Texture? _bottomRightTexture;
    private readonly string _markedFirstTexturePath = "Storage/marked_first";
    private Texture? _markedFirstTexture;
    private readonly string _markedSecondTexturePath = "Storage/marked_second";
    private Texture? _markedSecondTexture;
    #endregion

    public ItemGridPiece(Entity<ItemComponent> entity, ItemStorageLocation location,  IEntityManager entityManager)
    {
        IoCManager.InjectDependencies(this);

        _entityManager = entityManager;
        _storageController = UserInterfaceManager.GetUIController<StorageUIController>();

        Entity = entity.Owner;
        Location = location;

        Visible = true;
        MouseFilter = MouseFilterMode.Stop;

        TooltipSupplier = SupplyTooltip;

        OnThemeUpdated();
    }

    private Control? SupplyTooltip(Control sender)
    {
        if (_storageController.IsDragging)
            return null;

        return new Tooltip
        {
            Text = _entityManager.GetComponent<MetaDataComponent>(Entity).EntityName
        };
    }

    protected override void OnThemeUpdated()
    {
        base.OnThemeUpdated();

        _centerTexture = Theme.ResolveTextureOrNull(_centerTexturePath)?.Texture;
        _topTexture = Theme.ResolveTextureOrNull(_topTexturePath)?.Texture;
        _bottomTexture = Theme.ResolveTextureOrNull(_bottomTexturePath)?.Texture;
        _leftTexture = Theme.ResolveTextureOrNull(_leftTexturePath)?.Texture;
        _rightTexture = Theme.ResolveTextureOrNull(_rightTexturePath)?.Texture;
        _topLeftTexture = Theme.ResolveTextureOrNull(_topLeftTexturePath)?.Texture;
        _topRightTexture = Theme.ResolveTextureOrNull(_topRightTexturePath)?.Texture;
        _bottomLeftTexture = Theme.ResolveTextureOrNull(_bottomLeftTexturePath)?.Texture;
        _bottomRightTexture = Theme.ResolveTextureOrNull(_bottomRightTexturePath)?.Texture;
        _markedFirstTexture = Theme.ResolveTextureOrNull(_markedFirstTexturePath)?.Texture;
        _markedSecondTexture = Theme.ResolveTextureOrNull(_markedSecondTexturePath)?.Texture;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // really just an "oh shit" catch.
        if (!_entityManager.EntityExists(Entity) || !_entityManager.TryGetComponent<ItemComponent>(Entity, out var itemComponent))
        {
            Dispose();
            return;
        }

        if (IsDraggedElsewhere())
            return;

        DrawTiles(handle, itemComponent);

        if (!DeferIconDraw)
            DrawIcon(handle, Vector2.Zero);
    }

    /// <summary>
    /// True when another control is currently standing in for this entity as the drag ghost.
    /// </summary>
    private bool IsDraggedElsewhere()
    {
        return _storageController.IsDragging &&
               _storageController.DraggingGhost?.Entity == Entity &&
               _storageController.DraggingGhost != this;
    }

    /// <summary>
    /// Paints the opaque tiles that back the shape this item occupies in the grid.
    /// </summary>
    private void DrawTiles(DrawingHandleScreen handle, ItemComponent itemComponent)
    {
        var adjustedShape = _entityManager.System<ItemSystem>().GetAdjustedItemShape((Entity, itemComponent), Location.Rotation, Vector2i.Zero);
        var boundingGrid = adjustedShape.GetBoundingBox();
        var size = _centerTexture!.Size * 2 * UIScale;

        var hovering = !_storageController.IsDragging && UserInterfaceManager.CurrentlyHovered == this;
        //yeah, this coloring is kinda hardcoded. deal with it. B)
        Color? colorModulate = hovering  ? null : Color.FromHex("#a8a8a8");

        var marked = Marked != null;
        _markedPosition = null;

        _texturesPositions.Clear();
        for (var y = boundingGrid.Bottom; y <= boundingGrid.Top; y++)
        {
            for (var x = boundingGrid.Left; x <= boundingGrid.Right; x++)
            {
                if (!adjustedShape.Contains(x, y))
                    continue;

                var offset = size * 2 * new Vector2(x - boundingGrid.Left, y - boundingGrid.Bottom);
                // Draw calls are already transformed by this control's global position, so these
                // stay control-local. Adding Position on top would count the offset from the parent
                // twice over.
                var topLeft = offset.Floored();

                if (GetTexture(adjustedShape, new Vector2i(x, y), Direction.NorthEast) is {} neTexture)
                {
                    var neOffset = new Vector2(size.X, 0);
                    handle.DrawTextureRect(neTexture, new UIBox2(topLeft + neOffset, topLeft + neOffset + size), colorModulate);
                }
                if (GetTexture(adjustedShape, new Vector2i(x, y), Direction.NorthWest) is {} nwTexture)
                {
                    _texturesPositions.Add((nwTexture, offset / UIScale));
                    handle.DrawTextureRect(nwTexture, new UIBox2(topLeft, topLeft + size), colorModulate);

                    if (marked && nwTexture == _topLeftTexture)
                    {
                        _markedPosition = topLeft;
                        marked = false;
                    }
                }
                if (GetTexture(adjustedShape, new Vector2i(x, y), Direction.SouthEast) is {} seTexture)
                {
                    var seOffset = size;
                    handle.DrawTextureRect(seTexture, new UIBox2(topLeft + seOffset, topLeft + seOffset + size), colorModulate);
                }
                if (GetTexture(adjustedShape, new Vector2i(x, y), Direction.SouthWest) is {} swTexture)
                {
                    var swOffset = new Vector2(0, size.Y);
                    handle.DrawTextureRect(swTexture, new UIBox2(topLeft + swOffset, topLeft + swOffset + size), colorModulate);
                }
            }
        }
    }

    /// <summary>
    /// Paints this item's icon and marker badge.
    /// </summary>
    /// <param name="origin">
    /// This piece's top-left corner expressed in the pixels of whichever control is drawing right
    /// now. Zero when the piece draws itself, non-zero when the storage window draws it in its
    /// icon pass.
    /// </param>
    public void DrawIcon(DrawingHandleScreen handle, Vector2 origin)
    {
        if (!_entityManager.TryGetComponent<ItemComponent>(Entity, out var itemComponent) || IsDraggedElsewhere())
            return;

        var adjustedShape = _entityManager.System<ItemSystem>().GetAdjustedItemShape((Entity, itemComponent), Location.Rotation, Vector2i.Zero);
        var boundingGrid = adjustedShape.GetBoundingBox();
        var size = _centerTexture!.Size * 2 * UIScale;

        // typically you'd divide by two, but since the textures are half a tile, this is done implicitly
        var iconPosition = new Vector2((boundingGrid.Width + 1) * size.X + itemComponent.StoredOffset.X * 2,
            (boundingGrid.Height + 1) * size.Y + itemComponent.StoredOffset.Y * 2);
        var iconRotation = Location.Rotation + Angle.FromDegrees(itemComponent.StoredRotation);

        if (itemComponent.StoredSprite is { } storageSprite)
        {
            var scale = 2 * UIScale;
            var sprite = _entityManager.System<SpriteSystem>().Frame0(storageSprite);
            var spriteSize = new Vector2(sprite.Width, sprite.Height) * scale;

            // Centre the sprite on the icon position and spin it about that centre, the same as the
            // DrawEntity path below. Deriving the anchor from the sprite's own size is what keeps
            // the two paths agreeing for stored sprites that aren't a single 32x32 tile.
            var centre = GlobalPixelPosition + iconPosition;
            handle.SetTransform(centre, iconRotation);
            handle.DrawTextureRect(sprite, new UIBox2(-spriteSize / 2, spriteSize / 2));
            handle.SetTransform(GlobalPixelPosition - origin, Angle.Zero);
        }
        else
        {
            _entityManager.System<SpriteSystem>().ForceUpdate(Entity);
            handle.DrawEntity(Entity,
                origin + iconPosition,
                Vector2.One * 2 * UIScale,
                Angle.Zero,
                eyeRotation: iconRotation,
                overrideDirection: Direction.South);
        }

        if (_markedPosition is {} markedPos)
        {
            var markedTexture = Marked switch
            {
                ItemGridPieceMarks.First => _markedFirstTexture,
                ItemGridPieceMarks.Second => _markedSecondTexture,
                _ => null,
            };

            if (markedTexture != null)
            {
                handle.DrawTextureRect(markedTexture, new UIBox2(origin + markedPos, origin + markedPos + size));
            }
        }
    }

    protected override bool HasPoint(Vector2 point)
    {
        foreach (var (texture, position) in _texturesPositions)
        {
            if (!new Box2(position, position + texture.Size * 4).Contains(point))
                continue;

            return true;
        }

        return false;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        OnPiecePressed?.Invoke(args, this);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        OnPieceUnpressed?.Invoke(args, this);
    }

    private Texture? GetTexture(IReadOnlyList<Box2i> boxes, Vector2i position, Direction corner)
    {
        var top = !boxes.Contains(position - Vector2i.Up);
        var bottom = !boxes.Contains(position - Vector2i.Down);
        var left = !boxes.Contains(position + Vector2i.Left);
        var right = !boxes.Contains(position + Vector2i.Right);

        switch (corner)
        {
            case Direction.NorthEast:
                if (top && right)
                    return _topRightTexture;
                if (top)
                    return _topTexture;
                if (right)
                    return _rightTexture;
                return _centerTexture;
            case Direction.NorthWest:
                if (top && left)
                    return _topLeftTexture;
                if (top)
                    return _topTexture;
                if (left)
                    return _leftTexture;
                return _centerTexture;
            case Direction.SouthEast:
                if (bottom && right)
                    return _bottomRightTexture;
                if (bottom)
                    return _bottomTexture;
                if (right)
                    return _rightTexture;
                return _centerTexture;
            case Direction.SouthWest:
                if (bottom && left)
                    return _bottomLeftTexture;
                if (bottom)
                    return _bottomTexture;
                if (left)
                    return _leftTexture;
                return _centerTexture;
            default:
                return null;
        }
    }

    public static Vector2 GetCenterOffset(Entity<ItemComponent?> entity, ItemStorageLocation location, IEntityManager entMan)
    {
        var boxSize = entMan.System<ItemSystem>().GetAdjustedItemShape(entity, location).GetBoundingBox().Size;
        var actualSize = new Vector2(boxSize.X + 1, boxSize.Y + 1);
        return actualSize * new Vector2i(8, 8);
    }

    public EntityUid? UiEntity => Entity;
}

public enum ItemGridPieceMarks
{
    First,
    Second,
}
