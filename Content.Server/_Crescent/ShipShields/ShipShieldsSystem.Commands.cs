
using Robust.Shared.Console;
using Content.Server.Administration;
using Content.Shared.Administration;


namespace Content.Server._Crescent.ShipShields;
public partial class ShipShieldsSystem
{
    [Dependency] private readonly IConsoleHost _conHost = default!;

    public void InitializeCommands()
    {
        _conHost.RegisterCommand("shieldentity", "Create a shield around an entity", "shieldentity <uid>",
            ShieldEntityCmd);
        _conHost.RegisterCommand("unshieldentity", "Remove a shield from an entity", "unshieldentity <uid>",
            UnshieldEntityCmd);
    }

    [AdminCommand(AdminFlags.Debug)]
    public void ShieldEntityCmd(IConsoleShell shell, string argstr, string[] args)
    {
        // Eclipsion: args was indexed without a length check (a bare "shieldentity" threw), and the id an
        // admin reads off their client is a NetEntity, not a server-side EntityUid.
        if (args.Length < 1)
        {
            shell.WriteError("Usage: shieldentity <uid>");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netUid) || !TryGetEntity(netUid, out var entity))
        {
            shell.WriteError("Couldn't parse entity.");
            return;
        }

        var uid = entity.Value;
        var shield = ShieldEntity(uid);

        shell.WriteLine("Created shield " + shield);
    }

    [AdminCommand(AdminFlags.Debug)]
    public void UnshieldEntityCmd(IConsoleShell shell, string argstr, string[] args)
    {
        // Eclipsion: see ShieldEntityCmd - length guard plus NetEntity resolution.
        if (args.Length < 1)
        {
            shell.WriteError("Usage: unshieldentity <uid>");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netUid) || !TryGetEntity(netUid, out var entity))
        {
            shell.WriteError("Couldn't parse entity.");
            return;
        }

        var uid = entity.Value;
        var unshielded = UnshieldEntity(uid);

        if (unshielded)
            shell.WriteLine("Removed shield from " + uid);
        else
            shell.WriteError("No shield to remove from " + uid);
    }
}
