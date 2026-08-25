using Content.Server.Audio;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed class GridMusicCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IResourceManager _res = default!;

    public string Command => "gridmusic";
    public string Description => "Plays a sound for all players on a specific grid.";
    public string Help => "Usage: gridmusic <gridEntityId> <soundPath> [volume]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        // Eclipsion: the id an admin reads off their client is a NetEntity. Parsing it straight into an
        // EntityUid silently resolves to an unrelated entity on the server.
        if (!NetEntity.TryParse(args[0], out var gridNet) || !_entManager.TryGetEntity(gridNet, out var gridUidNullable))
        {
            shell.WriteError($"Invalid grid entity ID: {args[0]}");
            return;
        }

        var gridUid = gridUidNullable.Value;

        var soundPath = args[1];
        var audio = AudioParams.Default;

        if (args.Length >= 3 && int.TryParse(args[2], out var volume))
        {
            audio = audio.WithVolume(volume);
        }

        var filter = Filter.BroadcastGrid(gridUid);

        if (filter.Count == 0)
        {
            shell.WriteLine("No players found on the specified grid.");
            return;
        }

        _entManager.System<ServerGlobalSoundSystem>().PlayAdminGlobal(filter, soundPath, audio, false);
        shell.WriteLine($"Playing '{soundPath}' for {filter.Count} player(s) on grid {gridUid}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHint("Grid Entity ID");

        if (args.Length == 2)
        {
            var options = CompletionHelper.AudioFilePath(args[1], _protoManager, _res);
            return CompletionResult.FromHintOptions(options, "Sound path");
        }

        if (args.Length == 3)
            return CompletionResult.FromHint("Volume (optional, default 0)");

        return CompletionResult.Empty;
    }
}
