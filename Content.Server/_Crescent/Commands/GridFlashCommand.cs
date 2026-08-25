using Content.Server.Stunnable;
using Content.Shared.Flash;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed class GridFlashCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public string Command => "gridflash";
    public string Description => "Flashes all players on a grid for a duration, ignoring flash protection.";
    public string Help => "Usage: gridflash <gridEntityId> [durationSeconds=10]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
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

        var duration = 10f;
        if (args.Length >= 2 && float.TryParse(args[1], out var d))
            duration = d;

        var durationMs = duration * 1000f;
        var now = _timing.CurTime;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        var flashQuery = _entManager.GetEntityQuery<FlashableComponent>();
        var count = 0;

        var query = _entManager.EntityQueryEnumerator<FlashableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flashable, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            // Bypass all flash protection — set values directly
            flashable.LastFlash = now;
            flashable.Duration = duration;
            _entManager.Dirty(uid, flashable);

            // Also stun/slowdown
            var stunSystem = _entManager.System<StunSystem>();
            stunSystem.TrySlowdown(uid, TimeSpan.FromSeconds(duration), true, 0.2f, 0.2f);

            count++;
        }

        shell.WriteLine($"Flashed {count} entities on grid {gridUid} for {duration} seconds.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHint("Grid Entity ID");

        if (args.Length == 2)
            return CompletionResult.FromHint("Duration in seconds (default: 10)");

        return CompletionResult.Empty;
    }
}
