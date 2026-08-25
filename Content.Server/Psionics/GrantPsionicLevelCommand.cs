using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Abilities.Psionics;
using Robust.Shared.Console;

namespace Content.Server.Psionics;

/// <summary>
/// Admin/debug command for testing progression without waiting for a psionic roll event.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class GrantPsionicLevelCommand : IConsoleCommand
{
    public string Command => "grantpsioniclevel";
    public string Description => Loc.GetString("command-grant-psionic-level-description");
    public string Help => Loc.GetString("command-grant-psionic-level-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var entityManager = IoCManager.Resolve<IEntityManager>();

        if (!PsionicCommandHelper.TryResolveTarget(shell, args, entityManager, out var uid, out var psionic))
            return;

        var amount = 1;
        if (args.Length == 2 && (!int.TryParse(args[1], out amount) || amount <= 0))
        {
            shell.WriteError(Loc.GetString("command-grant-psionic-level-invalid-amount"));
            return;
        }

        entityManager.System<PsionicsSystem>().GrantPsionicLevel(uid, psionic, amount);

        shell.WriteLine(Loc.GetString(
            "command-grant-psionic-level-granted",
            ("amount", amount),
            ("target", entityManager.ToPrettyString(uid)),
            ("level", psionic.PsionicLevel),
            ("points", psionic.SkillPoints)));
    }
}
