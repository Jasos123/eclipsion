using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._Crescent.Territory;
using Robust.Shared.Console;

namespace Content.Server._Crescent.Territory;

/// <summary>
/// Admin control over persistent freeplay territory. Ownership survives round restarts and server restarts by
/// design, so a region left in a state nobody can play their way out of - a mining outpost stuck under a faction
/// that no longer fields anyone, or a save row for a region ID a mapper has since renamed - would otherwise stay
/// that way forever. This is the way out, without hand-editing capture_regions.json on a live server.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class TerritoryCommand : IConsoleCommand
{
    public string Command => "territory";
    public string Description => "Inspects and overrides ownership of persistent capture regions.";

    public string Help =>
        "Usage: territory <list|set|clear|forget>\n" +
        "  list                     - every region in the save or on the current map\n" +
        "  set <regionId> <faction> - hand a region to DSM, NCWL, TFSC or SHI\n" +
        "  clear <regionId>         - return a region to nobody\n" +
        "  forget <regionId>        - drop the save row, so the map's own owner applies next load";

    [Dependency] private readonly IEntitySystemManager _systems = default!;

    private static IReadOnlyList<string> Factions => PersistentTerritoryFactions.Supported;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        var territory = _systems.GetEntitySystem<PersistentCaptureRegionSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                ListRegions(shell, territory);
                return;

            case "set":
                if (args.Length < 3)
                {
                    shell.WriteError("Usage: territory set <regionId> <DSM|NCWL|TFSC|SHI>");
                    return;
                }

                if (!territory.SetOwner(args[1], args[2]))
                {
                    shell.WriteError($"'{args[2]}' cannot hold territory. Use one of: {string.Join(", ", Factions)}.");
                    return;
                }

                shell.WriteLine($"Region '{args[1]}' is now held by {args[2].Trim().ToUpperInvariant()}.");
                return;

            case "clear":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: territory clear <regionId>");
                    return;
                }

                territory.SetOwner(args[1], null);
                shell.WriteLine($"Region '{args[1]}' is now unclaimed.");
                return;

            case "forget":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: territory forget <regionId>");
                    return;
                }

                shell.WriteLine(territory.ForgetRegion(args[1])
                    ? $"Region '{args[1]}' dropped from the save."
                    : $"Region '{args[1]}' was not in the save.");
                return;

            default:
                shell.WriteLine(Help);
                return;
        }
    }

    private static void ListRegions(IConsoleShell shell, PersistentCaptureRegionSystem territory)
    {
        var regions = territory.GetRegions();
        if (regions.Count == 0)
        {
            shell.WriteLine("No persistent capture regions are saved or loaded.");
            return;
        }

        foreach (var region in regions)
        {
            shell.WriteLine(
                $"{region.RegionId,-28} {region.Owner ?? "-",-6} " +
                $"{(region.Loaded ? "loaded" : "saved "),-7} {region.Name}");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(["list", "set", "clear", "forget"], "action");

        var action = args[0].ToLowerInvariant();

        if (args.Length == 2 && action is "set" or "clear" or "forget")
        {
            var regions = _systems.GetEntitySystem<PersistentCaptureRegionSystem>()
                .GetRegions()
                .Select(region => region.RegionId);

            return CompletionResult.FromHintOptions(regions, "region ID");
        }

        if (args.Length == 3 && action == "set")
            return CompletionResult.FromHintOptions(Factions, "faction");

        return CompletionResult.Empty;
    }
}
