using System.Linq;
using Content.Client.Administration.Managers;
using Content.Client.ContextMenu.UI;
using Content.Client.Decals;
using Content.Client.Gameplay;
using Content.Client.Maps;
using Content.Client.SubFloor;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.Verbs;
using Content.Shared.Administration;
using Content.Shared.Decals;
using Content.Shared.Input;
using Content.Shared.Mapping;
using Content.Shared.Maps;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.OptionButton;
using static Robust.Shared.Input.Binding.PointerInputCmdHandler;
using Vector2 = System.Numerics.Vector2;

namespace Content.Client.Mapping;

public sealed class MappingState : GameplayStateBase
{
    [Dependency] private readonly IClientAdminManager _admin = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly IEntityNetworkManager _entityNetwork = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly ILogManager _log = default!;
    // SharedMapSystem is an entity system, not an IoC service.
    private SharedMapSystem MapManager => _entityManager.System<SharedMapSystem>();
    [Dependency] private readonly MappingManager _mapping = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
    [Dependency] private readonly ILocalizationManager _locale = default!;

    private EntityMenuUIController _entityMenuController = default!;

    private DecalPlacementSystem _decal = default!;
    private SpriteSystem _sprite = default!;
    private TransformSystem _transform = default!;
    private VerbSystem _verbs = default!;
    private GridDraggingSystem _gridDrag = default!;
    private MapSystem _map = default!;
    private SharedDecalSystem _sharedDecal = default!;

    // 1 off in case something else uses these colors since we use them to compare
    private static readonly Color PickColor = new(1, 255, 0);
    private static readonly Color DeleteColor = new(255, 1, 0);
    private static readonly Color EraseDecalColor = Color.Red.WithAlpha(0.2f);
    private static readonly Color GridSelectColor = Color.Green.WithAlpha(0.2f);
    private static readonly Color GridRemoveColor = Color.Red.WithAlpha(0.2f);

    private readonly ISawmill _sawmill;
    private readonly GameplayStateLoadController _loadController;
    private bool _setup;
    private readonly Dictionary<Type, List<MappingPrototype>> _allPrototypes = new();
    private readonly Dictionary<IPrototype, MappingPrototype> _allPrototypesDict = new();
    private readonly Dictionary<Type, Dictionary<string, MappingPrototype>> _idDict = new();
    private readonly Dictionary<IPrototype, List<Texture>> _textureCache = new();
    private (TimeSpan At, MappingSpawnButton Button)? _lastClicked;
    private (Control, MappingPrototypeList)? _scrollTo;
    private bool _tileErase;
    private int _decalIndex;

    private MappingScreen Screen => (MappingScreen) UserInterfaceManager.ActiveScreen!;
    private MainViewport Viewport => UserInterfaceManager.ActiveScreen!.GetWidget<MainViewport>()!;

    public CursorMeta Meta { get; }

    public MappingState()
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("mapping");
        _loadController = UserInterfaceManager.GetUIController<GameplayStateLoadController>();

        Meta = new CursorMeta();
    }

    protected override void Startup()
    {
        EnsureSetup();
        base.Startup();

        UserInterfaceManager.LoadScreen<MappingScreen>();
        _loadController.LoadScreen();

        var context = _input.Contexts.GetContext("common");
        context.AddFunction(ContentKeyFunctions.MappingUnselect);
        context.AddFunction(ContentKeyFunctions.SaveMap);
        context.AddFunction(ContentKeyFunctions.MappingEnablePick);
        context.AddFunction(ContentKeyFunctions.MappingEnableDecalPick);
        context.AddFunction(ContentKeyFunctions.MappingEnableDelete);
        context.AddFunction(ContentKeyFunctions.MappingPick);
        context.AddFunction(ContentKeyFunctions.MappingRemoveDecal);
        context.AddFunction(ContentKeyFunctions.MappingCancelEraseDecal);
        context.AddFunction(ContentKeyFunctions.MappingOpenContextMenu);
        context.AddFunction(ContentKeyFunctions.MouseMiddle);

        Screen.DecalSystem = _decal;

        Screen.Entities.GetPrototypeData += OnGetData;
        Screen.Entities.SelectionChanged += OnSelected;
        Screen.Tiles.GetPrototypeData += OnGetData;
        Screen.Tiles.SelectionChanged += OnSelected;
        Screen.Decals.GetPrototypeData += OnGetData;
        Screen.Decals.SelectionChanged += OnSelected;

        Screen.Pick.OnPressed += OnPickPressed;
        Screen.PickDecal.OnPressed += OnPickDecalPressed;
        Screen.EntityReplaceButton.OnToggled += OnEntityReplacePressed;
        Screen.EntityPlacementMode.OnItemSelected += OnEntityPlacementSelected;
        Screen.EraseEntityButton.OnToggled += OnEraseEntityPressed;
        Screen.EraseTileButton.OnToggled += OnEraseTilePressed;
        Screen.EraseDecalButton.OnToggled += OnEraseDecalPressed;
        Screen.FixGridAtmos.OnPressed += OnFixGridAtmosPressed;
        Screen.RemoveGrid.OnPressed += OnRemoveGridPressed;
        Screen.MoveGrid.OnPressed += OnMoveGridPressed;
        Screen.GridVV.OnPressed += OnGridVVPressed;
        Screen.GridScreenshot.OnPressed += OnGridScreenshotPressed;
        Screen.PipesColor.OnPressed += OnPipesColorPressed;
        Screen.ChatButton.OnPressed += OnChatButtonPressed;
        _placement.PlacementChanged += OnPlacementChanged;
        _mapping.OnFavoritePrototypesLoaded += OnFavoritesLoaded;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MappingUnselect, new PointerInputCmdHandler(HandleMappingUnselect, outsidePrediction: true))
            .Bind(ContentKeyFunctions.SaveMap, new PointerInputCmdHandler(HandleSaveMap, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnablePick, new PointerStateInputCmdHandler(HandleEnablePick, HandleDisablePick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnableDecalPick, new PointerStateInputCmdHandler(HandleEnableDecalPick, HandleDisableDecalPick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnableDelete, new PointerStateInputCmdHandler(HandleEnableDelete, HandleDisableDelete, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingPick, new PointerInputCmdHandler(HandlePick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingRemoveDecal, new PointerInputCmdHandler(HandleEditorCancelPlace, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingCancelEraseDecal, new PointerInputCmdHandler(HandleCancelEraseDecal, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingOpenContextMenu, new PointerInputCmdHandler(HandleOpenContextMenu, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MouseMiddle, new PointerInputCmdHandler(HandleMouseMiddle, outsidePrediction: true))
            .Bind(EngineKeyFunctions.Use, new PointerInputCmdHandler(HandleUse, outsidePrediction: true))
            .Register<MappingState>();

        _overlays.AddOverlay(new MappingOverlay(this));

        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;

        _mapping.LoadFavorites();
        ReloadPrototypes();
        UpdateLocale();
    }

    protected override void Shutdown()
    {
        SaveFavorites();
        CommandBinds.Unregister<MappingState>();

        Screen.Entities.GetPrototypeData -= OnGetData;
        Screen.Entities.SelectionChanged -= OnSelected;
        Screen.Tiles.GetPrototypeData -= OnGetData;
        Screen.Tiles.SelectionChanged -= OnSelected;
        Screen.Decals.GetPrototypeData -= OnGetData;
        Screen.Decals.SelectionChanged -= OnSelected;

        Screen.Pick.OnPressed -= OnPickPressed;
        Screen.PickDecal.OnPressed -= OnPickDecalPressed;
        Screen.EntityReplaceButton.OnToggled -= OnEntityReplacePressed;
        Screen.EntityPlacementMode.OnItemSelected -= OnEntityPlacementSelected;
        Screen.EraseEntityButton.OnToggled -= OnEraseEntityPressed;
        Screen.EraseTileButton.OnToggled -= OnEraseTilePressed;
        Screen.EraseDecalButton.OnToggled -= OnEraseDecalPressed;
        Screen.FixGridAtmos.OnPressed -= OnFixGridAtmosPressed;
        Screen.RemoveGrid.OnPressed -= OnRemoveGridPressed;
        Screen.MoveGrid.OnPressed -= OnMoveGridPressed;
        Screen.GridVV.OnPressed -= OnGridVVPressed;
        Screen.GridScreenshot.OnPressed -= OnGridScreenshotPressed;
        Screen.PipesColor.OnPressed -= OnPipesColorPressed;
        Screen.ChatButton.OnPressed -= OnChatButtonPressed;
        _placement.PlacementChanged -= OnPlacementChanged;
        _prototypeManager.PrototypesReloaded -= OnPrototypesReloaded;
        _mapping.OnFavoritePrototypesLoaded -= OnFavoritesLoaded;

        UserInterfaceManager.ClearWindows();
        _loadController.UnloadScreen();
        UserInterfaceManager.UnloadScreen();

        var context = _input.Contexts.GetContext("common");
        context.RemoveFunction(ContentKeyFunctions.MappingUnselect);
        context.RemoveFunction(ContentKeyFunctions.SaveMap);
        context.RemoveFunction(ContentKeyFunctions.MappingEnablePick);
        context.RemoveFunction(ContentKeyFunctions.MappingEnableDecalPick);
        context.RemoveFunction(ContentKeyFunctions.MappingEnableDelete);
        context.RemoveFunction(ContentKeyFunctions.MappingPick);
        context.RemoveFunction(ContentKeyFunctions.MappingRemoveDecal);
        context.RemoveFunction(ContentKeyFunctions.MappingCancelEraseDecal);
        context.RemoveFunction(ContentKeyFunctions.MappingOpenContextMenu);
        context.RemoveFunction(ContentKeyFunctions.MouseMiddle);

        _overlays.RemoveOverlay<MappingOverlay>();

        base.Shutdown();
    }

    private void EnsureSetup()
    {
        if (_setup)
            return;

        _setup = true;

        _entityMenuController = UserInterfaceManager.GetUIController<EntityMenuUIController>();

        _decal = _entityManager.System<DecalPlacementSystem>();
        _sprite = _entityManager.System<SpriteSystem>();
        _transform = _entityManager.System<TransformSystem>();
        _verbs = _entityManager.System<VerbSystem>();
        _gridDrag = _entityManager.System<GridDraggingSystem>();
        _map = _entityManager.System<MapSystem>();
        _sharedDecal = _entityManager.System<SharedDecalSystem>();
    }

    private void UpdateLocale()
    {
        if (_input.TryGetKeyBinding(ContentKeyFunctions.MappingEnablePick, out var enablePickBinding))
            Screen.Pick.ToolTip = Loc.GetString("mapping-pick-tooltip", ("key", enablePickBinding.GetKeyString()));

        if (_input.TryGetKeyBinding(ContentKeyFunctions.MappingEnableDecalPick, out var enableDecalPickBinding))
            Screen.PickDecal.ToolTip = Loc.GetString("mapping-pick-decal-tooltip", ("key", enableDecalPickBinding.GetKeyString()));

        if (_input.TryGetKeyBinding(ContentKeyFunctions.MappingEnableDelete, out var enableDeleteBinding))
            Screen.EraseEntityButton.ToolTip = Loc.GetString("mapping-erase-entity-tooltip", ("key", enableDeleteBinding.GetKeyString()));
    }

    private void SaveFavorites()
    {
        Screen.Entities.FavoritesPrototype.Children ??= new List<MappingPrototype>();
        Screen.Tiles.FavoritesPrototype.Children ??= new List<MappingPrototype>();
        Screen.Decals.FavoritesPrototype.Children ??= new List<MappingPrototype>();

        var children = Screen.Entities.FavoritesPrototype.Children
            .Union(Screen.Tiles.FavoritesPrototype.Children)
            .Union(Screen.Decals.FavoritesPrototype.Children)
            .ToList();

        _mapping.SaveFavorites(children);
    }

    private void ReloadPrototypes()
    {
        // These caches are rebuilt from scratch below. Without clearing them Register() hands back the mapping
        // objects of the previous run, which are still parented to the previous (now thrown away) top level
        // entries, so every list would come out empty after a prototype reload.
        _allPrototypes.Clear();
        _allPrototypesDict.Clear();
        _idDict.Clear();
        _textureCache.Clear();

        var entities = new MappingPrototype(null, Loc.GetString("mapping-entities")) { Children = new List<MappingPrototype>() };
        foreach (var entity in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            Register(entity, entity.ID, entities);
        }

        Sort(entities, _allPrototypes.GetOrNew(typeof(EntityPrototype)));

        var tiles = new MappingPrototype(null, Loc.GetString("mapping-tiles")) { Children = new List<MappingPrototype>() };
        foreach (var tile in _prototypeManager.EnumeratePrototypes<ContentTileDefinition>())
        {
            Register(tile, tile.ID, tiles);
        }

        Sort(tiles, _allPrototypes.GetOrNew(typeof(ContentTileDefinition)));

        var decals = new MappingPrototype(null, Loc.GetString("mapping-decals")) { Children = new List<MappingPrototype>() };
        foreach (var decal in _prototypeManager.EnumeratePrototypes<DecalPrototype>())
        {
            if (decal.ShowMenu)
                Register(decal, decal.ID, decals);
        }

        Sort(decals, _allPrototypes.GetOrNew(typeof(DecalPrototype)));

        var entitiesTemplate = new MappingPrototype(null, Loc.GetString("mapping-template"));
        var tilesTemplate = new MappingPrototype(null, Loc.GetString("mapping-template"));
        var decalsTemplate = new MappingPrototype(null, Loc.GetString("mapping-template"));

        foreach (var favorite in _prototypeManager.EnumeratePrototypes<MappingTemplatePrototype>())
        {
            switch (favorite.RootType)
            {
                case TemplateType.Entity:
                    RegisterTemplates(favorite, favorite.RootType, entitiesTemplate);
                    break;
                case TemplateType.Tile:
                    RegisterTemplates(favorite, favorite.RootType, tilesTemplate);
                    break;
                case TemplateType.Decal:
                    RegisterTemplates(favorite, favorite.RootType, decalsTemplate);
                    break;
            }
        }

        Sort(entitiesTemplate, recursive: false);
        Screen.Entities.UpdateVisible(
            new(entitiesTemplate.Children?.Count > 0 ? [entitiesTemplate, entities] : [entities]),
            _allPrototypes.GetOrNew(typeof(EntityPrototype)));

        Sort(tilesTemplate, recursive: false);
        Screen.Tiles.UpdateVisible(
            new(tilesTemplate.Children?.Count > 0 ? [tilesTemplate, tiles] : [tiles]),
            _allPrototypes.GetOrNew(typeof(ContentTileDefinition)));

        Sort(decalsTemplate, recursive: false);
        Screen.Decals.UpdateVisible(
            new(decalsTemplate.Children?.Count > 0 ? [decalsTemplate, decals] : [decals]),
            _allPrototypes.GetOrNew(typeof(DecalPrototype)));
    }

    private void RegisterTemplates(MappingTemplatePrototype templateProto, TemplateType? type, MappingPrototype toplevel)
    {
        if (type == null)
        {
            if (templateProto.RootType == null)
                return;
            type = templateProto.RootType;
        }

        MappingPrototype? proto = null;
        switch (type)
        {
            case TemplateType.Decal:
                if (_idDict.GetOrNew(typeof(DecalPrototype)).TryGetValue(templateProto.ID, out var decal))
                    proto = decal;
                break;
            case TemplateType.Tile:
                if (_idDict.GetOrNew(typeof(ContentTileDefinition)).TryGetValue(templateProto.ID, out var tile))
                    proto = tile;
                break;
            case TemplateType.Entity:
                if (_idDict.GetOrNew(typeof(EntityPrototype)).TryGetValue(templateProto.ID, out var entity))
                    proto = entity;
                break;
        }

        if (proto == null)
        {
            var name = templateProto.ID;
            if (_locale.TryGetString($"mapping-template-{templateProto.ID.ToLower()}", out var locale))
                name = locale;
            proto = new MappingPrototype(null, name);
        }

        proto.Parents ??= new List<MappingPrototype>();
        proto.Parents.Add(toplevel);

        foreach (var child in templateProto.Children)
        {
            RegisterTemplates(child, type, proto);
        }

        toplevel.Children ??= new List<MappingPrototype>();
        toplevel.Children.Add(proto);
    }

    private MappingPrototype? Register<T>(T? prototype, string id, MappingPrototype topLevel) where T : class, IPrototype, IInheritingPrototype
    {
        {
            if (prototype == null &&
                _prototypeManager.TryIndex(id, out prototype) &&
                prototype is EntityPrototype entity)
            {
                if (entity.HideSpawnMenu || entity.Abstract)
                    prototype = null;
            }
        }

        if (prototype == null)
        {
            if (!_prototypeManager.TryGetMapping(typeof(T), id, out var node))
            {
                _sawmill.Error($"No {typeof(T).Name} found with id {id}");
                return null;
            }

            var ids = _idDict.GetOrNew(typeof(T));
            if (ids.TryGetValue(id, out var mapping))
            {
                return mapping;
            }
            else
            {
                var name = node.TryGet("name", out ValueDataNode? nameNode)
                    ? nameNode.Value
                    : id;

                if (string.IsNullOrWhiteSpace(name))
                    name = id;

                if (node.TryGet("suffix", out ValueDataNode? suffix))
                    name = $"{name} [{suffix.Value}]";

                mapping = new MappingPrototype(prototype, name);
                _allPrototypes.GetOrNew(typeof(T)).Add(mapping);
                ids.Add(id, mapping);

                if (node.TryGet("parent", out ValueDataNode? parentValue))
                {
                    var parent = Register<T>(null, parentValue.Value, topLevel);

                    if (parent != null)
                    {
                        mapping.Parents ??= new List<MappingPrototype>();
                        mapping.Parents.Add(parent);
                        parent.Children ??= new List<MappingPrototype>();
                        parent.Children.Add(mapping);
                    }
                }
                else if (node.TryGet("parent", out SequenceDataNode? parentSequence))
                {
                    foreach (var parentNode in parentSequence.Cast<ValueDataNode>())
                    {
                        var parent = Register<T>(null, parentNode.Value, topLevel);

                        if (parent != null)
                        {
                            mapping.Parents ??= new List<MappingPrototype>();
                            mapping.Parents.Add(parent);
                            parent.Children ??= new List<MappingPrototype>();
                            parent.Children.Add(mapping);
                        }
                    }
                }
                else
                {
                    topLevel.Children ??= new List<MappingPrototype>();
                    topLevel.Children.Add(mapping);
                    mapping.Parents ??= new List<MappingPrototype>();
                    mapping.Parents.Add(topLevel);
                }

                return mapping;
            }
        }
        else
        {
            var ids = _idDict.GetOrNew(typeof(T));
            if (ids.TryGetValue(id, out var mapping))
            {
                return mapping;
            }
            else
            {
                var entity = prototype as EntityPrototype;

                // EntityPrototype.Name is an empty string, not null, when a prototype has no name: the old
                // `?? ID` never fired for those and they showed up as a blank row you can't identify.
                var name = entity?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    name = prototype.ID;

                if (!string.IsNullOrWhiteSpace(entity?.EditorSuffix))
                    name = $"{name} [{entity.EditorSuffix}]";

                mapping = new MappingPrototype(prototype, name);
                _allPrototypes.GetOrNew(typeof(T)).Add(mapping);
                _allPrototypesDict.Add(prototype, mapping);
                ids.Add(prototype.ID, mapping);
            }

            if (prototype.Parents == null)
            {
                topLevel.Children ??= new List<MappingPrototype>();
                topLevel.Children.Add(mapping);
                mapping.Parents ??= new List<MappingPrototype>();
                mapping.Parents.Add(topLevel);
                return mapping;
            }

            foreach (var parentId in prototype.Parents)
            {
                var parent = Register<T>(null, parentId, topLevel);

                if (parent != null)
                {
                    mapping.Parents ??= new List<MappingPrototype>();
                    mapping.Parents.Add(parent);
                    parent.Children ??= new List<MappingPrototype>();
                    parent.Children.Add(mapping);
                }
            }

            return mapping;
        }
    }

    private static int Compare(MappingPrototype a, MappingPrototype b)
    {
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="recursive">
    ///     False for the template lists: their contents are hand-ordered in yaml (most used first) and
    ///     alphabetising them would throw that away. Only their top level gets sorted.
    /// </param>
    private void Sort(MappingPrototype topLevel, List<MappingPrototype>? prototypes = null, bool recursive = true)
    {
        topLevel.Children ??= new List<MappingPrototype>();

        if (prototypes != null)
        {
            foreach (var prototype in prototypes)
            {
                if (prototype.Parents != null || prototype == topLevel)
                    continue;

                prototype.Parents = new List<MappingPrototype> { topLevel };
                topLevel.Children.Add(prototype);
            }
        }

        if (!recursive)
        {
            topLevel.Children.Sort(Compare);
            return;
        }

        // Sort the whole tree, not just its first level: children get registered in prototype load order,
        // which is shuffled, so every collapsed group would otherwise be in a different order each round.
        SortRecursive(topLevel, new HashSet<MappingPrototype>());
    }

    private static void SortRecursive(MappingPrototype prototype, HashSet<MappingPrototype> sorted)
    {
        // The same prototype shows up under each of its parents, so only sort each one once.
        if (!sorted.Add(prototype))
            return;

        prototype.Parents?.Sort(Compare);

        if (prototype.Children == null)
            return;

        prototype.Children.Sort(Compare);

        foreach (var child in prototype.Children)
        {
            SortRecursive(child, sorted);
        }
    }

    private void Deselect()
    {
        if (Screen.Entities.Selected is { } entitySelected)
        {
            entitySelected.Button.Pressed = false;
            Screen.Entities.Selected = null;

            if (entitySelected.Prototype?.Prototype is EntityPrototype)
                _placement.Clear();
        }

        if (Screen.Tiles.Selected is { } tileSelected)
        {
            tileSelected.Button.Pressed = false;
            Screen.Tiles.Selected = null;

            if (tileSelected.Prototype?.Prototype is ContentTileDefinition)
                _placement.Clear();
        }

        if (Screen.Decals.Selected is { } decalSelected)
        {
            decalSelected.Button.Pressed = false;
            Screen.Decals.Selected = null;

            if (decalSelected.Prototype?.Prototype is DecalPrototype)
                _decal.SetActive(false);
        }
    }

    /// <summary>
    ///     Switches to a tool button, turning off whatever eraser was running first.
    /// </summary>
    /// <remarks>
    ///     <see cref="MappingScreen.UnPressActionsExcept"/> only moves the buttons: setting Pressed doesn't
    ///     raise anything, so the eraser it unpressed would keep erasing, or worse, tear down the tool that
    ///     just replaced it a frame later. Everything that takes over the cursor goes through here instead.
    /// </remarks>
    private void SelectTool(Control button)
    {
        // These tools have state outside their buttons. Programmatically unpressing a button does not
        // invoke its click handler, so explicitly tear that state down when another tool takes over.
        if (button != Screen.MoveGrid && _gridDrag.Enabled)
            _consoleHost.ExecuteCommand("griddrag");

        if (button != Screen.PipesColor)
            _entitySystemManager.GetEntitySystem<SubFloorHideSystem>().ShowAll = false;

        if (button != Screen.EraseEntityButton)
            DisableEntityEraser();

        if (button != Screen.EraseTileButton)
            DisableTileEraser();

        Screen.UnPressActionsExcept(button);
    }

    private void EnableEntityEraser()
    {
        Deselect();
        SelectTool(Screen.EraseEntityButton);

        if (!_placement.Eraser)
        {
            _placement.Clear();
            _placement.ToggleEraser();
        }

        Screen.EraseEntityButton.Pressed = true;
        Screen.EntityPlacementMode.Disabled = true;

        Meta.State = CursorState.Entity;
        Meta.Color = DeleteColor;
    }

    private void DisableEntityEraser()
    {
        if (_placement.Eraser)
            _placement.ToggleEraser();

        Screen.EraseEntityButton.Pressed = false;
        Screen.EntityPlacementMode.Disabled = _tileErase;

        if (Meta.State == CursorState.Entity && Meta.Color == DeleteColor)
            Meta.State = CursorState.None;
    }

    private void DisableTileEraser()
    {
        if (!_tileErase)
            return;

        _tileErase = false;
        Screen.EraseTileButton.Pressed = false;
        Screen.EntityPlacementMode.Disabled = _placement.Eraser;

        // Only drop the placement if it is still the tile eraser. Something else may have taken it over
        // already, and clearing that would cancel the mapper's brand new selection instead.
        if (_placement.CurrentPermission is { IsTile: true, TileType: 0 })
            _placement.Clear();
    }

    #region On Event
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<EntityPrototype>() &&
            !obj.WasModified<ContentTileDefinition>() &&
            !obj.WasModified<DecalPrototype>() &&
            !obj.WasModified<MappingTemplatePrototype>())
        {
            return;
        }

        SaveFavorites();

        // Reloading rebuilds every mapping prototype, so the favorites have to be looked up again afterwards.
        // Otherwise they keep pointing at objects of the old tree and quietly stop working.
        var favorites = GetFavoritePrototypes();
        ReloadPrototypes();
        OnFavoritesLoaded(favorites);
    }

    private List<IPrototype> GetFavoritePrototypes()
    {
        var favorites = new List<IPrototype>();

        foreach (var list in new[] { Screen.Entities, Screen.Tiles, Screen.Decals })
        {
            if (list.FavoritesPrototype.Children is not { } children)
                continue;

            foreach (var child in children)
            {
                if (child.Prototype is { } prototype)
                    favorites.Add(prototype);
            }
        }

        return favorites;
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (!_placement.IsActive && _decal.GetActiveDecal().Decal == null)
            Deselect();

        // The button state used to be assigned here, but the placement manager raises this *before* it
        // resets its own fields, so Eraser still reads true while it is in the middle of becoming false -
        // which is what left the erase button lit up over a placement that wasn't erasing anything.
        // SyncTools() does it from the settled state instead.
    }

    private void OnFavoritesLoaded(List<IPrototype> prototypes)
    {
        Screen.Entities.FavoritesPrototype.Children = new List<MappingPrototype>();
        Screen.Decals.FavoritesPrototype.Children = new List<MappingPrototype>();
        Screen.Tiles.FavoritesPrototype.Children = new List<MappingPrototype>();

        foreach (var prototype in prototypes)
        {
            switch (prototype)
            {
                case EntityPrototype entityPrototype:
                    {
                        if (_idDict.GetOrNew(typeof(EntityPrototype)).TryGetValue(entityPrototype.ID, out var entity))
                        {
                            Screen.Entities.FavoritesPrototype.Children.Add(entity);
                            entity.Parents ??= new List<MappingPrototype>();
                            entity.Parents.Add(Screen.Entities.FavoritesPrototype);
                            entity.Favorite = true;
                        }
                        break;
                    }
                case DecalPrototype decalPrototype:
                    {
                        if (_idDict.GetOrNew(typeof(DecalPrototype)).TryGetValue(decalPrototype.ID, out var decal))
                        {
                            Screen.Decals.FavoritesPrototype.Children.Add(decal);
                            decal.Parents ??= new List<MappingPrototype>();
                            decal.Parents.Add(Screen.Decals.FavoritesPrototype);
                            decal.Favorite = true;
                        }
                        break;
                    }
                case ContentTileDefinition tileDefinition:
                    {
                        if (_idDict.GetOrNew(typeof(ContentTileDefinition)).TryGetValue(tileDefinition.ID, out var tile))
                        {
                            Screen.Tiles.FavoritesPrototype.Children.Add(tile);
                            tile.Parents ??= new List<MappingPrototype>();
                            tile.Parents.Add(Screen.Tiles.FavoritesPrototype);
                            tile.Favorite = true;
                        }
                        break;
                    }
            }
        }
    }

    protected override void OnKeyBindStateChanged(ViewportBoundKeyEventArgs args)
    {
        if (args.Viewport == null)
            base.OnKeyBindStateChanged(new ViewportBoundKeyEventArgs(args.KeyEventArgs, Viewport.Viewport));
        else
            base.OnKeyBindStateChanged(args);

        UpdateLocale();
    }

    private void OnGetData(IPrototype prototype, List<Texture> textures)
    {
        // Getting an entity's textures spawns and deletes a dummy entity, and the search list rebuilds its
        // buttons on every scroll tick, so this has to be cached or scrolling turns into a spawn storm.
        if (_textureCache.TryGetValue(prototype, out var cached))
        {
            textures.AddRange(cached);
            return;
        }

        var result = new List<Texture>();

        try
        {
            switch (prototype)
            {
                case EntityPrototype entity:
                    result.AddRange(SpriteComponent.GetPrototypeTextures(entity, _resources).Select(t => t.Default));
                    break;
                case DecalPrototype decal:
                    result.Add(_sprite.Frame0(decal.Sprite));
                    break;
                case ContentTileDefinition tile:
                    if (tile.Sprite?.ToString() is { } sprite)
                        result.Add(_resources.GetResource<TextureResource>(sprite).Texture);
                    break;
            }
        }
        catch (Exception e)
        {
            // One prototype with a broken sprite must not take the whole list down with it: this runs in
            // the middle of building the list, and throwing here leaves it half-built and unusable.
            _sawmill.Error($"Failed to get the textures of {prototype.ID}:\n{e}");
            result.Clear();
        }

        _textureCache[prototype] = result;
        textures.AddRange(result);
    }

    private void OnSelected(MappingPrototypeList list, MappingPrototype mapping)
    {
        if (mapping.Prototype == null)
            return;

        var chain = new Stack<MappingPrototype>();
        chain.Push(mapping);

        var parent = mapping.Parents?.FirstOrDefault();
        while (parent != null)
        {
            chain.Push(parent);
            parent = parent.Parents?.FirstOrDefault();
        }

        _lastClicked = null;

        Control? last = null;
        var children = list.PrototypeList.Children.ToList();
        foreach (var prototype in chain)
        {
            foreach (var child in children)
            {
                if (child is MappingSpawnButton button &&
                    button.Prototype == prototype)
                {
                    button.CollapseButton.Pressed = true;
                    list.ToggleCollapse(button);
                    OnSelected(list, button, prototype.Prototype);
                    children = button.ChildrenPrototypes.Children.ToList();
                    children.AddRange(button.ChildrenPrototypesGallery.Children);
                    last = child;
                    break;
                }
            }
        }

        if (last != null && list.PrototypeList.Visible)
            _scrollTo = (last, list);
    }

    private void OnSelected(MappingPrototypeList list, MappingSpawnButton button, IPrototype? prototype)
    {
        var time = _timing.CurTime;
        if (prototype is DecalPrototype)
            Screen.SelectDecal(prototype.ID);

        // Double-click functionality if it's collapsible.
        if (_lastClicked is { } lastClicked &&
            lastClicked.Button == button &&
            lastClicked.At > time - TimeSpan.FromSeconds(0.333) &&
            string.IsNullOrEmpty(list.SearchBar.Text) &&
            button.CollapseButton.Visible)
        {
            button.CollapseButton.Pressed = !button.CollapseButton.Pressed;
            list.ToggleCollapse(button);
            button.Button.Pressed = true;
            list.Selected = button;
            _lastClicked = null;
            return;
        }

        // Toggle if it's the same button (at least if we just unclicked it).
        if (!button.Button.Pressed && button.Prototype?.Prototype != null && _lastClicked?.Button == button)
        {
            _lastClicked = null;
            Deselect();
            return;
        }

        _lastClicked = (time, button);

        if (button.Prototype == null)
            return;

        if (list.Selected is { } oldButton &&
            oldButton != button)
        {
            Deselect();
        }

        Meta.State = CursorState.None;
        // Picking something from the list ends every tool, erasers included - otherwise the tile eraser
        // would tear down the placement this is about to start, one frame later.
        SelectTool(new Control());

        switch (prototype)
        {
            case EntityPrototype entity:
                {
                    var placementId = Screen.EntityPlacementMode.SelectedId;

                    var placement = new PlacementInformation
                    {
                        PlacementOption = placementId > 0 ? EntitySpawnWindow.InitOpts[placementId] : entity.PlacementMode,
                        EntityType = entity.ID,
                        IsTile = false
                    };

                    _decal.SetActive(false);
                    _placement.BeginPlacing(placement);
                    break;
                }
            case DecalPrototype decal:
                _placement.Clear();

                _decal.SetActive(true);
                Screen.SelectDecal(decal.ID);
                break;
            case ContentTileDefinition tile:
                {
                    var placement = new PlacementInformation
                    {
                        PlacementOption = "AlignTileAny",
                        TileType = tile.TileId,
                        IsTile = true
                    };

                    _decal.SetActive(false);
                    _placement.BeginPlacing(placement);
                    break;
                }
            default:
                _placement.Clear();
                break;
        }

        list.Selected = button;

        button.Button.Pressed = true;
    }

    private void OnEntityReplacePressed(ButtonToggledEventArgs args)
    {
        _placement.Replacement = args.Pressed;
    }

    private void OnEntityPlacementSelected(ItemSelectedEventArgs args)
    {
        Screen.EntityPlacementMode.SelectId(args.Id);

        if (_placement.CurrentMode != null)
        {
            var placement = new PlacementInformation
            {
                PlacementOption = EntitySpawnWindow.InitOpts[args.Id],
                EntityType = _placement.CurrentPermission!.EntityType,
                TileType = _placement.CurrentPermission.TileType,
                Range = 2,
                IsTile = _placement.CurrentPermission.IsTile,
            };

            _placement.BeginPlacing(placement);
        }
    }

    private void OnEraseEntityPressed(ButtonEventArgs args)
    {
        // No early out on args.Button.Pressed == _placement.Eraser: if the two ever disagree that is exactly
        // the state that needs fixing, and skipping it is what made the button need a second click.
        if (args.Button.Pressed)
            EnableEntityEraser();
        else
            DisableEntityEraser();
    }

    private void OnEraseTilePressed(ButtonEventArgs args)
    {
        Meta.State = CursorState.None;

        if (!args.Button.Pressed)
        {
            _tileErase = false;
            _placement.Clear();
            Deselect();
            Screen.EntityPlacementMode.Disabled = _placement.Eraser;
            return;
        }

        SelectTool(Screen.EraseTileButton);
        _placement.Clear();
        Deselect();

        _placement.BeginPlacing(new PlacementInformation
        {
            PlacementOption = "AlignTileAny",
            TileType = 0,
            Range = 400,
            IsTile = true,
        });

        Screen.EraseTileButton.Pressed = true;
        _tileErase = true;
        Screen.EntityPlacementMode.Disabled = true;
    }

    private void OnEraseDecalPressed(ButtonToggledEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Meta.State = CursorState.Tile;
            Meta.Color = EraseDecalColor;

            SelectTool(Screen.EraseDecalButton);
            _placement.Clear();
            Deselect();
            Screen.EraseDecalButton.Pressed = true;
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }
    #endregion

    #region Mapping Actions
    private void OnPickPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            EnablePick();
        else
            DisablePick();
    }

    private void EnablePick()
    {
        Deselect();
        SelectTool(Screen.Pick);
        Meta.State = CursorState.EntityOrTile;
        Meta.Color = PickColor;
        Meta.SecondColor = PickColor.WithAlpha(0.2f);
    }

    private void DisablePick()
    {
        Screen.Pick.Pressed = false;
        Meta.State = CursorState.None;
    }

    private void OnPickDecalPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Decal;
            Meta.Color = PickColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }

    private void OnFixGridAtmosPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Grid;
            Meta.Color = GridSelectColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }

    private void OnRemoveGridPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Grid;
            Meta.Color = GridRemoveColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }

    private void OnMoveGridPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Grid;
            Meta.Color = GridSelectColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }

        var gridDragSystem = _entitySystemManager.GetEntitySystem<GridDraggingSystem>();
        if (args.Button.Pressed != gridDragSystem.Enabled)
        {
            _consoleHost.ExecuteCommand("griddrag");
        }
    }

    private void OnGridVVPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Grid;
            Meta.Color = GridSelectColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }

    private void OnGridScreenshotPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Grid;
            Meta.Color = GridSelectColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }

    private void OnPipesColorPressed(ButtonEventArgs args)
    {
        _entitySystemManager.GetEntitySystem<SubFloorHideSystem>().ShowAll = args.Button.Pressed;

        if (args.Button.Pressed)
        {
            Deselect();
            Meta.State = CursorState.Entity;
            Meta.Color = PickColor;
            SelectTool(args.Button);
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }

    private void OnChatButtonPressed(ButtonEventArgs args)
    {
        Screen.Chat.Visible = args.Button.Pressed;
    }
    #endregion

    #region Handle Bindings
    private bool HandleOpenContextMenu(in PointerInputCmdArgs args)
    {
        Deselect();

        var coords = _transform.ToMapCoordinates(args.Coordinates);
        if (_verbs.TryGetEntityMenuEntities(coords, out var entities))
            _entityMenuController.OpenRootMenu(entities);

        return true;
    }

    private bool HandleMappingUnselect(in PointerInputCmdArgs args)
    {
        if (Screen.MoveGrid.Pressed && _gridDrag.Enabled)
        {
            _consoleHost.ExecuteCommand("griddrag");
        }

        SelectTool(new Control());
        Meta.State = CursorState.None;

        if (Screen.Decals.Selected is not { Prototype.Prototype: DecalPrototype })
            return false;

        Deselect();
        return true;
    }

    private bool HandleSaveMap(in PointerInputCmdArgs args)
    {
        // No FULL_RELEASE check here: saving was compiled out of published builds, which made the save
        // hotkey do nothing at all on the live server. The server checks these same permissions again.
        if (!_admin.IsAdmin(true) || (!_admin.HasFlag(AdminFlags.Host) && !_admin.HasFlag(AdminFlags.Mapping)))
            return false;

        SaveMap();
        return true;
    }

    private bool HandleEnablePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        EnablePick();
        return true;
    }

    private bool HandleDisablePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        DisablePick();
        return true;
    }

    private bool HandleEnableDecalPick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        Deselect();
        Screen.PickDecal.Pressed = true;
        Meta.State = CursorState.Decal;
        Meta.Color = PickColor;
        SelectTool(Screen.PickDecal);
        return true;
    }

    private bool HandleDisableDecalPick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        Screen.PickDecal.Pressed = false;
        Meta.State = CursorState.None;
        return true;
    }

    private bool HandleEnableDelete(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        Screen.EraseEntityButton.Pressed = true;
        EnableEntityEraser();
        return true;
    }

    private bool HandleDisableDelete(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        Screen.EraseEntityButton.Pressed = false;
        DisableEntityEraser();
        return true;
    }

    private bool HandlePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        MappingPrototype? button = null;

        if (Screen.Pick.Pressed)
        {
            if (!uid.IsValid())
            {
                var mapPos = _transform.ToMapCoordinates(coords);

                if (MapManager.TryFindGridAt(mapPos, out var gridUid, out var grid) &&
                    _entityManager.System<SharedMapSystem>().TryGetTileRef(gridUid, grid, coords, out var tileRef) &&
                    _allPrototypesDict.TryGetValue(tileRef.GetContentTileDefinition(), out button))
                {
                    switch (button.Prototype)
                    {
                        case EntityPrototype:
                            {
                                OnSelected(Screen.Entities, button);
                                break;
                            }
                        case ContentTileDefinition:
                            {
                                OnSelected(Screen.Tiles, button);
                                break;
                            }
                    }

                    return true;
                }
            }
        }
        else if (Screen.PickDecal.Pressed)
        {
            if (GetHoveredDecal() is { } decal &&
                _prototypeManager.TryIndex<DecalPrototype>(decal.Id, out var decalProto) &&
                _allPrototypesDict.TryGetValue(decalProto, out button))
            {
                OnSelected(Screen.Decals, button);
                Screen.SelectDecal(decal);
                return true;
            }
        }
        else
        {
            return false;
        }

        if (button != null)
            return false;

        if (uid == EntityUid.Invalid ||
            _entityManager.GetComponentOrNull<MetaDataComponent>(uid) is not
            { EntityPrototype: { } prototype } ||
            !_allPrototypesDict.TryGetValue(prototype, out button))
        {
            // we always block other input handlers if pick mode is enabled
            // this makes you not accidentally place something in space because you
            // miss-clicked while holding down the pick hotkey
            return true;
        }

        // Selected an entity
        OnSelected(Screen.Entities, button);

        // Match rotation
        _placement.Direction = _entityManager.GetComponent<TransformComponent>(uid).LocalRotation.GetDir();

        return true;
    }

    private bool HandleEditorCancelPlace(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!Screen.EraseDecalButton.Pressed)
            return false;

        _entityNetwork.SendSystemNetworkMessage(new RequestDecalRemovalEvent(_entityManager.GetNetCoordinates(coords)));
        return true;
    }

    private bool HandleCancelEraseDecal(in PointerInputCmdArgs args)
    {
        if (!Screen.EraseDecalButton.Pressed)
            return false;

        Screen.EraseDecalButton.Pressed = false;
        return true;
    }

    private bool HandleUse(in PointerInputCmdArgs args)
    {
        if (Screen.FixGridAtmos.Pressed)
        {
            Screen.FixGridAtmos.Pressed = false;
            Meta.State = CursorState.None;
            if (GetHoveredGrid() is { } grid)
                _consoleHost.ExecuteCommand($"fixgridatmos {_entityManager.GetNetEntity(grid.Owner).Id}");

            return true;
        }

        if (Screen.RemoveGrid.Pressed)
        {
            Screen.RemoveGrid.Pressed = false;
            Meta.State = CursorState.None;
            if (GetHoveredGrid() is { } grid)
                _consoleHost.ExecuteCommand($"rmgrid {_entityManager.GetNetEntity(grid.Owner).Id}");

            return true;
        }

        if (Screen.GridVV.Pressed)
        {
            Screen.GridVV.Pressed = false;
            Meta.State = CursorState.None;
            if (GetHoveredGrid() is { } grid)
                _consoleHost.ExecuteCommand($"vv {_entityManager.GetNetEntity(grid.Owner).Id}");

            return true;
        }

        if (Screen.GridScreenshot.Pressed)
        {
            Screen.GridScreenshot.Pressed = false;
            Meta.State = CursorState.None;
            if (GetHoveredGrid() is { } grid)
                ExportGridScreenshot(grid);

            return true;
        }

        if (Screen.PipesColor.Pressed)
        {
            Screen.PipesColor.Pressed = false;
            Meta.State = CursorState.None;
            if (GetHoveredEntity() is { } entity)
                _consoleHost.ExecuteCommand($"colornetwork {_entityManager.GetNetEntity(entity).Id} Pipe {Screen.DecalColor.ToHex()}");

            return true;
        }

        return false;
    }

    private async void ExportGridScreenshot(Entity<MapGridComponent> grid)
    {
        Screen.GridScreenshot.Disabled = true;

        try
        {
            await _mapping.ExportGridScreenshot(grid);
        }
        catch (Exception ex)
        {
            _sawmill.Error("Failed to export grid {0} as PNG: {1}", grid.Owner, ex);
        }
        finally
        {
            // The mapping state may have closed while the native save dialog was open.
            if (UserInterfaceManager.ActiveScreen is MappingScreen screen)
                screen.GridScreenshot.Disabled = false;
        }
    }

    private bool HandleMouseMiddle(in PointerInputCmdArgs args)
    {
        if (Screen.PickDecal.Pressed)
        {
            _decalIndex += 1;
            return true;
        }

        if (_decal.GetActiveDecal() is { Decal: not null })
        {
            Screen.ChangeDecalRotation(90f);
            return true;
        }

        return false;
    }
    #endregion

    private async void SaveMap()
    {
        await _mapping.SaveMap();
    }

    public EntityUid? GetHoveredEntity()
    {
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } position)
        {
            return null;
        }

        var mapPos = viewport.PixelToMap(position.Position);
        return GetClickedEntity(mapPos);
    }

    public Entity<MapGridComponent>? GetHoveredGrid()
    {
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } position)
        {
            return null;
        }

        var mapPos = viewport.PixelToMap(position.Position);
        if (MapManager.TryFindGridAt(mapPos, out var gridUid, out var grid))
        {
            return new Entity<MapGridComponent>(gridUid, grid);
        }

        return null;
    }

    public Box2Rotated? GetHoveredTileBox2()
    {
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } coords)
        {
            return null;
        }

        if (GetHoveredGrid() is not { } grid)
            return null;

        if (!_entityManager.TryGetComponent<TransformComponent>(grid, out var xform))
            return null;

        var mapCoords = viewport.PixelToMap(coords.Position);
        var tileSize = grid.Comp.TileSize;
        var tileDimensions = new Vector2(tileSize, tileSize);
        var tileRef = _map.GetTileRef(grid, mapCoords);
        var worldCoord = _map.LocalToWorld(grid.Owner, grid.Comp, tileRef.GridIndices);
        var box = Box2.FromDimensions(worldCoord, tileDimensions);

        return new Box2Rotated(box, xform.LocalRotation, box.BottomLeft);
    }

    private Decal? GetHoveredDecal()
    {
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } coords)
        {
            return null;
        }

        if (GetHoveredGrid() is not { } grid)
            return null;

        var mapCoords = viewport.PixelToMap(coords.Position);
        var localCoords = _map.WorldToLocal(grid.Owner, grid.Comp, mapCoords.Position);
        var bounds = Box2.FromDimensions(localCoords, new Vector2(1.05f, 1.05f)).Translated(new Vector2(-1, -1));
        var decals = _sharedDecal.GetDecalsIntersecting(grid.Owner, bounds);

        if (decals.FirstOrDefault() is not { Decal: not null })
            return null;

        if (!decals.ToList().TryGetValue(_decalIndex % decals.Count, out var decal))
            return null;

        _decalIndex %= decals.Count;
        return decal.Decal;
    }

    public (Texture, Box2Rotated)? GetHoveredDecalData()
    {
        if (GetHoveredGrid() is not { } grid ||
            !_entityManager.TryGetComponent<TransformComponent>(grid, out var xform))
            return null;

        if (GetHoveredDecal() is not { } decal ||
            !_prototypeManager.TryIndex<DecalPrototype>(decal.Id, out var decalProto))
            return null;

        var worldCoords = _map.LocalToWorld(grid.Owner, grid.Comp, decal.Coordinates);
        var texture = _sprite.Frame0(decalProto.Sprite);
        var box = Box2.FromDimensions(worldCoords, new Vector2(1, 1));
        return (texture, new Box2Rotated(box, decal.Angle + xform.LocalRotation, box.BottomLeft));
    }

    /// <summary>
    ///     Keeps the tool buttons honest about what is actually running.
    /// </summary>
    /// <remarks>
    ///     The placement manager can be cleared from outside this state (hotkeys, the engine placing an
    ///     entity, another UI), and its change event fires too early to be trusted, so the settled state is
    ///     read once a frame. Assigning Pressed raises nothing, so this can't fight the click handlers.
    /// </remarks>
    private void SyncTools()
    {
        if (Screen.EraseEntityButton.Pressed != _placement.Eraser)
        {
            Screen.EraseEntityButton.Pressed = _placement.Eraser;

            if (!_placement.Eraser && Meta.State == CursorState.Entity && Meta.Color == DeleteColor)
                Meta.State = CursorState.None;
        }

        var tileEraserActive = _placement.IsActive
            && _placement.CurrentPermission is { IsTile: true, TileType: 0 };

        if (_tileErase && (!Screen.EraseTileButton.Pressed || !tileEraserActive))
            DisableTileEraser();

        Screen.EntityPlacementMode.Disabled = _placement.Eraser || _tileErase;
    }

    public override void FrameUpdate(FrameEventArgs e)
    {
        SyncTools();

        if (_scrollTo is not { } scrollTo)
            return;

        var (control, list) = scrollTo;

        // this is not ideal but we wait until the control's height is computed to use
        // its position to scroll to
        if (control.Height > 0 && list.PrototypeList.Visible)
        {
            var y = control.GlobalPosition.Y - list.ScrollContainer.Height / 2 + control.Height - list.GlobalPosition.Y;
            var scroll = list.ScrollContainer;
            scroll.SetScrollValue(scroll.GetScrollValue() + new Vector2(0, y));
            _scrollTo = null;
        }
    }


    public enum CursorState
    {
        None,
        Tile,
        Decal,
        Entity,
        Grid,
        EntityOrTile,
    }

    public sealed class CursorMeta
    {
        /// <summary>
        ///     Defines how the overlay will be rendered
        /// </summary>
        public CursorState State = CursorState.None;

        /// <summary>
        ///     Color with which the mapping overlay will be drawn
        /// </summary>
        public Color Color = Color.White;

        public Color? SecondColor;
    }
}
