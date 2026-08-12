using System.IO;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Mapping;

/// <summary>
///     Handles autosaving maps.
/// </summary>
public sealed class MappingSystem : EntitySystem
{
    [Dependency] private readonly IConsoleHost _conHost = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IResourceManager _resMan = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;

    /// <summary>
    ///     Suffix on every autosave file. Also used to tell autosaves apart from anything else a mapper
    ///     might have dropped in the folder, so rotation never deletes files it didn't create.
    /// </summary>
    private const string AutosaveSuffix = "-AUTO.yml";

    /// <summary>
    ///     Zero padded so that sorting the file names alphabetically also sorts them oldest-first.
    /// </summary>
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>
    ///     Folder name used for maps that were created from scratch instead of loaded from a file.
    /// </summary>
    private const string DefaultName = "NEWMAP";

    // Not a comp because I don't want to deal with this getting saved onto maps ever
    /// <summary>
    ///     map id -> next autosave timespan & the folder its saves go into.
    /// </summary>
    private readonly Dictionary<MapId, (TimeSpan Next, string Name)> _currentlyAutosaving = new();

    private bool _autosaveEnabled;

    public override void Initialize()
    {
        base.Initialize();

        _conHost.RegisterCommand("toggleautosave",
            Loc.GetString("cmd-toggleautosave-desc"),
            Loc.GetString("cmd-toggleautosave-help"),
            ToggleAutosaveCommand);

        Subs.CVar(_cfg, CCVars.AutosaveEnabled, SetAutosaveEnabled, true);
    }

    private void SetAutosaveEnabled(bool b)
    {
        if (!b)
            _currentlyAutosaving.Clear();
        _autosaveEnabled = b;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_autosaveEnabled || _currentlyAutosaving.Count == 0)
            return;

        foreach (var (map, (next, name)) in _currentlyAutosaving.ToArray())
        {
            if (_timing.RealTime <= next)
                continue;

            if (!_map.MapExists(map) || _map.IsInitialized(map))
            {
                Log.Warning($"Can't autosave map {map}; it doesn't exist, or is initialized. Removing from autosave.");
                _currentlyAutosaving.Remove(map);
                continue;
            }

            // Reschedule before saving, so a map that fails to save retries next interval instead of every tick.
            _currentlyAutosaving[map] = (CalculateNextTime(), name);
            Save(map, name);
        }
    }

    private void Save(MapId map, string name)
    {
        var directory = GetSaveDirectory(name);
        var path = directory / $"{DateTime.Now.ToString(TimestampFormat)}{AutosaveSuffix}";

        Log.Info($"Autosaving map {name} ({map}) to {path}. Next save in {ReadableTimeLeft(map)} seconds.");

        try
        {
            _resMan.UserData.CreateDir(directory);

            if (!_loader.TrySaveMap(map, path))
            {
                Log.Error($"Failed to autosave map {name} ({map}) to {path}!");
                return;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Caught exception while autosaving map {name} ({map}) to {path}:\n{e}");
            return;
        }

        PruneOldSaves(directory);
    }

    /// <summary>
    ///     Deletes the oldest autosaves of a map until only <see cref="CCVars.AutosaveRotation"/> of them are left.
    /// </summary>
    private void PruneOldSaves(ResPath directory)
    {
        var keep = _cfg.GetCVar(CCVars.AutosaveRotation);
        if (keep <= 0)
            return;

        try
        {
            var saves = _resMan.UserData.DirectoryEntries(directory)
                .Where(entry => entry.EndsWith(AutosaveSuffix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < saves.Count - keep; i++)
            {
                var old = directory / saves[i];
                _resMan.UserData.Delete(old);
                Log.Info($"Deleted old autosave {old}.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Caught exception while rotating old autosaves in {directory}:\n{e}");
        }
    }

    private ResPath GetSaveDirectory(string name)
    {
        return new ResPath(_cfg.GetCVar(CCVars.AutosaveDirectory)).ToRootedPath() / name;
    }

    /// <summary>
    ///     Turns whatever a mapper passed as a map path into something usable as a single folder name.
    /// </summary>
    private static string GetFolderName(string path)
    {
        var name = new ResPath(path.Replace('\\', '/')).FilenameWithoutExtension;

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? DefaultName : name;
    }

    private TimeSpan CalculateNextTime()
    {
        return _timing.RealTime + TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.AutosaveInterval));
    }

    private double ReadableTimeLeft(MapId map)
    {
        return Math.Round(_currentlyAutosaving[map].Next.TotalSeconds - _timing.RealTime.TotalSeconds);
    }

    #region Public API

    /// <summary>
    ///     How long between two autosaves of the same map.
    /// </summary>
    public TimeSpan AutosaveInterval => TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.AutosaveInterval));

    /// <summary>
    ///     The folder a map's autosaves are written to, or null if the map isn't being autosaved.
    /// </summary>
    public ResPath? GetAutosaveDirectory(MapId map)
    {
        return _currentlyAutosaving.TryGetValue(map, out var autosave) ? GetSaveDirectory(autosave.Name) : null;
    }

    /// <summary>
    ///     Starts autosaving a map, or restarts it if it was already being autosaved.
    /// </summary>
    /// <param name="map">The map to save. Must exist and must not be initialized.</param>
    /// <param name="path">Path the map was loaded from. Only its file name is used, as the save folder.</param>
    /// <returns>False if the map can't be autosaved, or autosaving is disabled entirely.</returns>
    public bool StartAutosave(MapId map, string? path = null)
    {
        if (!_autosaveEnabled)
            return false;

        if (!_map.MapExists(map) || _map.IsInitialized(map))
        {
            Log.Warning("Tried to enable autosaving on non-existant or already initialized map!");
            return false;
        }

        var name = GetFolderName(path ?? DefaultName);

        // Deliberately an overwrite and not a TryAdd: map ids get reused, so a stale entry from a deleted
        // map must never stop a fresh one from being saved.
        _currentlyAutosaving[map] = (CalculateNextTime(), name);
        Log.Info($"Started autosaving map {name} ({map}) to {GetSaveDirectory(name)}. Next save in {ReadableTimeLeft(map)} seconds.");
        return true;
    }

    /// <summary>
    ///     Stops autosaving a map.
    /// </summary>
    /// <returns>False if the map wasn't being autosaved in the first place.</returns>
    public bool StopAutosave(MapId map)
    {
        if (!_currentlyAutosaving.Remove(map))
            return false;

        Log.Info($"Stopped autosaving on map {map}");
        return true;
    }

    public void ToggleAutosave(MapId map, string? path = null)
    {
        if (StopAutosave(map))
            return;

        StartAutosave(map, path);
    }

    #endregion

    #region Commands

    [AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
    private void ToggleAutosaveCommand(IConsoleShell shell, string argstr, string[] args)
    {
        if (args.Length != 1 && args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!int.TryParse(args[0], out var intMapId))
        {
            shell.WriteError(Loc.GetString("cmd-mapping-failure-integer", ("arg", args[0])));
            return;
        }

        var mapId = new MapId(intMapId);

        if (StopAutosave(mapId))
        {
            shell.WriteLine(Loc.GetString("cmd-toggleautosave-stopped", ("mapId", mapId)));
            return;
        }

        if (!_autosaveEnabled)
        {
            shell.WriteError(Loc.GetString("cmd-toggleautosave-disabled"));
            return;
        }

        var path = args.Length == 2 ? args[1] : null;

        if (!StartAutosave(mapId, path) || GetAutosaveDirectory(mapId) is not { } directory)
        {
            shell.WriteError(Loc.GetString("cmd-toggleautosave-failed", ("mapId", mapId)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-toggleautosave-started",
            ("mapId", mapId),
            ("path", directory.ToString()),
            ("minutes", Math.Round(AutosaveInterval.TotalMinutes, 1))));
    }

    #endregion
}
