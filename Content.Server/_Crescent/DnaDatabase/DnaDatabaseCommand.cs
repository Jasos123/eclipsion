using Content.Server.Administration;
using Content.Shared._Crescent.DnaDatabase;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server._Crescent.DnaDatabase;

[AdminCommand(AdminFlags.Fun)]
public sealed class DnaDatabaseCommand : IConsoleCommand
{
    public string Command => "dnadb";
    public string Description => "Toggle DNA database recruitment on a station.";
    public string Help => "Usage: dnadb <stationEntityId> [on|off] — toggles or sets recruitment state.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteLine(Help);
            return;
        }

        var entManager = IoCManager.Resolve<IEntityManager>();

        // Eclipsion: the id an admin reads off their client is a NetEntity, not a server-side EntityUid.
        if (!NetEntity.TryParse(args[0], out var stationNet) || !entManager.TryGetEntity(stationNet, out var station))
        {
            shell.WriteError($"Invalid station entity ID: {args[0]}");
            return;
        }

        var stationUid = station.Value;
        var system = entManager.System<DnaDatabaseSystem>();

        Entity<DnaDatabaseComponent>? database = null;
        var query = entManager.EntityQueryEnumerator<DnaDatabaseComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.BoundStation != stationUid)
                continue;

            database = (uid, comp);
            break;
        }

        if (database == null)
        {
            shell.WriteError("No DNA database found for that station.");
            return;
        }

        if (args.Length >= 2)
        {
            var enable = args[1].ToLowerInvariant() is "on" or "1" or "true";
            if (database.Value.Comp.Enabled != enable)
                system.ApplyToggle(database.Value);
        }
        else
        {
            system.ApplyToggle(database.Value);
        }

        shell.WriteLine($"DNA database is now {(database.Value.Comp.Enabled ? "enabled" : "disabled")}.");
    }
}
