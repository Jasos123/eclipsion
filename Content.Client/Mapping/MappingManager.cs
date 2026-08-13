using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Content.Shared.Decals;
using Content.Shared.Mapping;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Mapping;

public sealed class MappingManager : IPostInjectInit
{
    /// <summary>
    ///     Smallest grid screenshot. Grids smaller than this get padding rather than a tiny image.
    /// </summary>
    private static readonly Vector2i MinScreenshotSize = new(1920, 1080);

    /// <summary>
    ///     Ceiling on either side of the render target. Big enough that a station still reads clearly, small
    ///     enough that allocating the viewport's render targets doesn't blow up video memory.
    /// </summary>
    private const int MaxScreenshotDimension = 4096;

    /// <summary>
    ///     Entities per tick the client accepts while streaming a grid in for a screenshot. Far above the normal
    ///     budget, which exists to keep gameplay smooth and would otherwise take minutes on a station.
    /// </summary>
    private const int ScreenshotPvsBudget = 2048;

    private static readonly TimeSpan ScreenshotPvsPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ScreenshotPvsTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How many polls in a row the entity count has to hold still before the grid counts as fully streamed in.
    /// </summary>
    private const int ScreenshotPvsStablePolls = 5;

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IFileDialogManager _file = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IClientNetManager _net = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private ISawmill _sawmill = default!;

    private Stream? _saveStream;
    private bool _savePending;
    private List<IPrototype>? _favoritePrototypes;

    public event Action<List<IPrototype>>? OnFavoritePrototypesLoaded;

    public void PostInject()
    {
        _sawmill = _log.GetSawmill("mapping");

        _net.RegisterNetMessage<MappingSaveMapMessage>();
        _net.RegisterNetMessage<MappingSaveMapErrorMessage>(OnSaveError);
        _net.RegisterNetMessage<MappingMapDataMessage>(OnMapData);
        _net.RegisterNetMessage<MappingFavoritesDataMessage>(OnFavoritesData);
        _net.RegisterNetMessage<MappingFavoritesSaveMessage>();
        _net.RegisterNetMessage<MappingScreenshotPvsMessage>();
    }

    private async void OnSaveError(MappingSaveMapErrorMessage message)
    {
        _sawmill.Error("Server failed to serialize the map. Check the server log for the reason.");
        var stream = _saveStream;
        _saveStream = null;
        _savePending = false;

        if (stream == null)
            return;

        try
        {
            await stream.DisposeAsync();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to close the map save destination:\n{e}");
        }
    }

    private async void OnMapData(MappingMapDataMessage message)
    {
        if (_saveStream is not { } stream)
        {
            _sawmill.Warning("Received map data without an active save request; discarding it.");
            _savePending = false;
            return;
        }

        // Claim the stream before awaiting so another response cannot write to the same file.
        _saveStream = null;

        try
        {
            // Maps are not ASCII: entity names, decal ids and map metadata can all contain non-ASCII characters,
            // and encoding those as ASCII silently replaces them with '?' and corrupts the saved file.
            await stream.WriteAsync(Encoding.UTF8.GetBytes(message.Yml));
            await stream.FlushAsync();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to write the saved map:\n{e}");
        }
        finally
        {
            try
            {
                await stream.DisposeAsync();
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to close the map save destination:\n{e}");
            }
            finally
            {
                _savePending = false;
            }
        }
    }

    private void OnFavoritesData(MappingFavoritesDataMessage message)
    {
        _favoritePrototypes = new List<IPrototype>();

        foreach (var prototype in message.PrototypeIDs)
        {
            if (_prototypeManager.TryIndex<EntityPrototype>(prototype, out var entity))
                _favoritePrototypes.Add(entity);
            else if (_prototypeManager.TryIndex<ContentTileDefinition>(prototype, out var tile))
                _favoritePrototypes.Add(tile);
            else if (_prototypeManager.TryIndex<DecalPrototype>(prototype, out var decal))
                _favoritePrototypes.Add(decal);
        }

        OnFavoritePrototypesLoaded?.Invoke(_favoritePrototypes);
    }

    public async Task SaveMap()
    {
        // The protocol has no request id, so overlapping requests cannot be matched to their responses.
        // Serialize saves instead of risking the first response being written to the second request's file.
        if (_savePending)
        {
            _sawmill.Warning("A map save is already in progress.");
            return;
        }

        _savePending = true;

        (Stream fileStream, bool alreadyExisted)? path;
        try
        {
            path = await _file.SaveFile();
        }
        catch
        {
            _savePending = false;
            throw;
        }

        if (path is not { fileStream: var stream })
        {
            _savePending = false;
            return;
        }

        _saveStream = stream;
        try
        {
            // Ask for the snapshot only after the destination exists. Besides avoiding needless server work
            // when the dialog is cancelled, this guarantees even an immediate response has a stream to use.
            _net.ClientSendMessage(new MappingSaveMapMessage());
        }
        catch
        {
            _saveStream = null;
            _savePending = false;

            try
            {
                await stream.DisposeAsync();
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to close the map save destination:\n{e}");
            }

            throw;
        }
    }

    /// <summary>
    ///     Renders a complete grid into a separate viewport and lets the mapper save it as PNG.
    ///     This does not resize or move the main mapping viewport.
    /// </summary>
    public async Task ExportGridScreenshot(Entity<MapGridComponent> grid)
    {
        if (!_entityManager.TryGetComponent<TransformComponent>(grid.Owner, out var xform) ||
            grid.Comp.LocalAABB.IsEmpty())
        {
            _sawmill.Error("Unable to export the selected grid because it no longer exists or is empty.");
            return;
        }

        var transform = _entityManager.System<SharedTransformSystem>();
        var bounds = transform.GetWorldMatrix(xform).TransformBox(grid.Comp.LocalAABB);

        // Leave room for walls and other sprites that extend beyond the outermost tile.
        var padding = MathF.Max(grid.Comp.TileSize, bounds.MaxDimension * 0.015f);
        bounds = bounds.Enlarged(padding);

        // Grow the render target with the grid instead of squeezing a whole station into 1920x1080, where a
        // single tile ends up a handful of pixels wide and small items are no longer recognisable.
        var size = new Vector2i(
            Math.Clamp((int) MathF.Ceiling(bounds.Width * EyeManager.PixelsPerMeter), MinScreenshotSize.X, MaxScreenshotDimension),
            Math.Clamp((int) MathF.Ceiling(bounds.Height * EyeManager.PixelsPerMeter), MinScreenshotSize.Y, MaxScreenshotDimension));

        var visibleWorldWidth = size.X / (float) EyeManager.PixelsPerMeter;
        var visibleWorldHeight = size.Y / (float) EyeManager.PixelsPerMeter;
        var zoom = MathF.Max(bounds.Width / visibleWorldWidth, bounds.Height / visibleWorldHeight);
        zoom = MathF.Max(zoom, 0.01f);

        var path = await _file.SaveFile(new FileDialogFilters(new FileDialogFilters.Group("png")));
        if (path is not { fileStream: var stream })
            return;

        await using (stream)
        {
            var netGrid = _entityManager.GetNetEntity(grid.Owner);
            var oldSpawnBudget = _cfg.GetCVar(CVars.NetPVSEntityBudget);
            var oldEnterBudget = _cfg.GetCVar(CVars.NetPVSEntityEnterBudget);

            try
            {
                // The renderer can only draw what the client has, and PVS only ever sends the entities within
                // net.pvs_range of the mapper's own eye. On anything bigger than that box the far half of the ship
                // was never on the client at all, which is why parts of it came out empty. Ask the server for the
                // whole grid, and lift the per-tick spawn budget so it arrives in a few ticks instead of a minute.
                _cfg.SetCVar(CVars.NetPVSEntityBudget, ScreenshotPvsBudget);
                _cfg.SetCVar(CVars.NetPVSEntityEnterBudget, ScreenshotPvsBudget);
                SetScreenshotPvs(netGrid, true);

                await WaitForGridEntities(grid.Owner);

                var eye = new FixedEye
                {
                    Position = new MapCoordinates(bounds.Center, xform.MapID),
                    Zoom = new Vector2(zoom),
                    DrawFov = false,
                    DrawLight = true,
                };

                using var viewport = _clyde.CreateViewport(size, "MappingGridScreenshot");
                viewport.Eye = eye;
                viewport.Render();

                var screenshotSource = new TaskCompletionSource<Image<Rgba32>>();

                viewport.RenderTarget.CopyPixelsToMemory<Rgba32>(image => screenshotSource.SetResult(image));

                using var screenshot = await screenshotSource.Task;
                await Task.Run(() => screenshot.SaveAsPng(stream));
                await stream.FlushAsync();

                _sawmill.Info("Exported grid {0} as a {1}x{2} PNG.", grid.Owner, size.X, size.Y);
            }
            finally
            {
                // Put the mapper back on normal streaming even if the render or the save threw, otherwise they
                // keep receiving the entire grid for the rest of the session.
                SetScreenshotPvs(netGrid, false);
                _cfg.SetCVar(CVars.NetPVSEntityBudget, oldSpawnBudget);
                _cfg.SetCVar(CVars.NetPVSEntityEnterBudget, oldEnterBudget);
            }
        }
    }

    private void SetScreenshotPvs(NetEntity grid, bool enabled)
    {
        _net.ClientSendMessage(new MappingScreenshotPvsMessage
        {
            Grid = grid,
            Enabled = enabled,
        });
    }

    /// <summary>
    ///     Waits for the server to finish streaming the grid in. There is no "you have it all now" signal, so
    ///     this settles for the entity count on the grid holding still for a few polls in a row.
    /// </summary>
    private async Task WaitForGridEntities(EntityUid grid)
    {
        var polls = (int) (ScreenshotPvsTimeout / ScreenshotPvsPollInterval);
        var previous = -1;
        var stable = 0;

        for (var i = 0; i < polls; i++)
        {
            await Task.Delay(ScreenshotPvsPollInterval);

            var count = CountGridEntities(grid);

            if (count != previous)
            {
                previous = count;
                stable = 0;
                continue;
            }

            if (++stable >= ScreenshotPvsStablePolls)
                return;
        }

        _sawmill.Warning(
            "Timed out waiting for grid {0} to finish streaming in; the screenshot may be missing entities.",
            grid);
    }

    private int CountGridEntities(EntityUid grid)
    {
        // Entities that PVS has taken away are detached from their parent, so they stop counting towards this.
        var count = 0;
        var query = _entityManager.AllEntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var xform))
        {
            if (xform.GridUid == grid)
                count++;
        }

        return count;
    }

    public void SaveFavorites(List<MappingPrototype> prototypes)
    {
        // TODO: that doesnt save null prototypes (mapping templates and abstract parents)
        var msg = new MappingFavoritesSaveMessage()
        {
            PrototypeIDs = prototypes
                .FindAll(p => p.Prototype != null)
                .Select(p => p.Prototype!.ID)
                .ToList(),
        };
        _net.ClientSendMessage(msg);
    }

    public void LoadFavorites()
    {
        var request = new MappingFavoritesLoadMessage();
        _net.ClientSendMessage(request);
    }
}
