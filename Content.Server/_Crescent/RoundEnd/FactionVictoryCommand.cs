using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Crescent.RoundEnd;

/// <summary>
/// Emergency admin override for a conquest round whose automatic victory detection became stuck.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed class FactionVictoryCommand : IConsoleCommand
{
    public string Command => "factionvictory";
    public string Description => "Awards the active conquest round to a faction and immediately ends the round.";
    public string Help => $"Usage: {Command} <faction>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var entityManager = IoCManager.Resolve<IEntityManager>();
        var conquest = entityManager.System<FactionConquestRuleSystem>();

        if (!conquest.TryForceVictory(args[0], out var winner, out var error))
        {
            shell.WriteError(error);
            return;
        }

        var actor = shell.Player?.Name ?? "Server console";
        IoCManager.Resolve<IChatManager>()
            .SendAdminAnnouncement($"{actor} forced a {winner} faction victory and ended the round.");
        shell.WriteLine($"Faction victory forced for {winner}. The round is ending.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var factions = IoCManager.Resolve<IEntityManager>()
            .System<FactionConquestRuleSystem>()
            .GetForceableFactions();
        return CompletionResult.FromHintOptions(factions, "<faction>");
    }
}
