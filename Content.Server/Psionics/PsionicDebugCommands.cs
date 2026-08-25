using Content.Server.Administration;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Psionics;

/// <summary>
/// Debug commands for exercising psionic powers without playing a Psion up to them first.
/// </summary>
/// <remarks>
/// Both default to the caller's own body when given no argument, because the usual reason to reach
/// for these is testing a power on yourself.
/// </remarks>
[AdminCommand(AdminFlags.Fun)]
public sealed class GrantPsionicPointsCommand : IConsoleCommand
{
    public string Command => "grantpsionicpoints";
    public string Description => Loc.GetString("command-grant-psionic-points-description");
    public string Help => Loc.GetString("command-grant-psionic-points-help");

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
            shell.WriteError(Loc.GetString("command-grant-psionic-points-invalid-amount"));
            return;
        }

        entityManager.System<PsionicSkillTreeSystem>().GrantSkillPoints(uid, psionic, amount);

        shell.WriteLine(Loc.GetString(
            "command-grant-psionic-points-granted",
            ("amount", amount),
            ("target", entityManager.ToPrettyString(uid)),
            ("points", psionic.SkillPoints)));
    }
}

/// <summary>
/// Clears the use delay on every action a psionic power granted, and nothing else. Rejuvenate
/// already clears all cooldowns, but it also heals, wakes and cures the target, which is far more
/// than "let me press the button again".
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class ResetPsionicCooldownsCommand : IConsoleCommand
{
    public string Command => "resetpsioniccooldowns";
    public string Description => Loc.GetString("command-reset-psionic-cooldowns-description");
    public string Help => Loc.GetString("command-reset-psionic-cooldowns-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var entityManager = IoCManager.Resolve<IEntityManager>();

        if (!PsionicCommandHelper.TryResolveTarget(shell, args, entityManager, out var uid, out var psionic))
            return;

        var actions = entityManager.System<SharedActionsSystem>();
        var cleared = 0;

        foreach (var powerActions in psionic.Actions.Values)
        {
            foreach (var action in powerActions)
            {
                // The list can hold nulls for powers whose action failed to spawn, and an action
                // entity outlives nothing - but a power removed mid-round can leave a stale id.
                if (action is not { } actionUid || entityManager.Deleted(actionUid))
                    continue;

                actions.ClearCooldown(actionUid);
                cleared++;
            }
        }

        shell.WriteLine(Loc.GetString(
            "command-reset-psionic-cooldowns-cleared",
            ("count", cleared),
            ("target", entityManager.ToPrettyString(uid))));
    }
}

internal static class PsionicCommandHelper
{
    /// <summary>
    /// Reads the optional first argument as the target entity, falling back to the caller's own
    /// body, and confirms it is psionic.
    /// </summary>
    public static bool TryResolveTarget(
        IConsoleShell shell,
        string[] args,
        IEntityManager entityManager,
        out EntityUid uid,
        out PsionicComponent psionic)
    {
        uid = default;
        psionic = default!;

        if (args.Length == 0)
        {
            if (shell.Player?.AttachedEntity is not { } self)
            {
                shell.WriteError(Loc.GetString("command-psionic-no-self"));
                return false;
            }

            uid = self;
        }
        else if (!NetEntity.TryParse(args[0], out var netEntity)
                 || !entityManager.TryGetEntity(netEntity, out var resolved))
        {
            // Admins read the *net* id off the client (VV, entity menu, Copy UID). Parsing it as a
            // raw server-side EntityUid silently lands on an unrelated entity.
            shell.WriteError(Loc.GetString("command-psionic-invalid-entity"));
            return false;
        }
        else
        {
            uid = resolved.Value;
        }

        if (!entityManager.TryGetComponent(uid, out PsionicComponent? component))
        {
            shell.WriteError(Loc.GetString(
                "command-psionic-not-psionic",
                ("target", entityManager.ToPrettyString(uid))));
            return false;
        }

        psionic = component;
        return true;
    }
}
