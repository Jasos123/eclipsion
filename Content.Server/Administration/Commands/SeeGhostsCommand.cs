using Content.Server.Ghost;
using Content.Shared.Administration;
using Content.Shared.Ghost;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server.Administration.Commands;

/// <summary>
///     Shows ghosts to the admin running the command and nobody else.
/// </summary>
/// <remarks>
///     The existing "showghosts" command calls <see cref="GhostSystem.MakeVisible"/>, which moves every
///     ghost off the Ghost visibility layer and onto the Normal one - that reveals them to every player
///     on the server and cannot be undone. This command instead widens only the caller's own eye mask,
///     so it is private to them and can be toggled back off.
/// </remarks>
[AdminCommand(AdminFlags.Admin)]
public sealed class SeeGhostsCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public override string Command => "seeghosts";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        if (player.AttachedEntity is not { } uid)
        {
            shell.WriteError(Loc.GetString("shell-must-be-attached-to-entity"));
            return;
        }

        if (_entities.HasComponent<GhostComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-seeghosts-already-ghost"));
            return;
        }

        if (!_entities.HasComponent<EyeComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-seeghosts-no-eye"));
            return;
        }

        var ghosts = _entities.System<GhostSystem>();

        bool enabled;
        switch (args.Length)
        {
            case 0:
                enabled = !ghosts.HasGhostVision(uid);
                break;
            case 1 when bool.TryParse(args[0], out var parsed):
                enabled = parsed;
                break;
            case 1:
                shell.WriteError(Loc.GetString("shell-invalid-bool"));
                return;
            default:
                shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
                return;
        }

        // Report the resulting state even when nothing changed, so a repeated call is not confusing.
        if (!ghosts.SetGhostVision(uid, enabled))
            enabled = ghosts.HasGhostVision(uid);

        shell.WriteLine(Loc.GetString(enabled
            ? "cmd-seeghosts-enabled"
            : "cmd-seeghosts-disabled"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.Booleans, Loc.GetString("cmd-seeghosts-hint"))
            : CompletionResult.Empty;
    }
}
