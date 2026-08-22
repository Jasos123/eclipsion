using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.PointCannons;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server.PointCannons;

[AdminCommand(AdminFlags.Mapping)]
public sealed class GenerateCannonSafetyRangesCommand : IConsoleCommand
{
    public string Command => "pc_genranges";
    public string Description => "Generates safety ranges for all cannons on specified grid.";
    public string Help => "Specify grid's entity uid.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError("Please specify grid uid.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Please specify radius of check (1 = 1 meter ingame)");
            return;
        }

        // Eclipsion: the id an admin reads off their client is a NetEntity, not a server-side EntityUid.
        if (NetEntity.TryParse(args[0], out var gridNet)
            && IoCManager.Resolve<IEntityManager>().TryGetEntity(gridNet, out var gridUidNullable))
        {
            var gridUid = gridUidNullable.Value;
            if (!int.TryParse(args[1], out int number))
                return;
            int count = 0;
            IEntityManager entMan = IoCManager.Resolve<IEntityManager>();
            PointCannonSystem cannonSys = entMan.System<PointCannonSystem>();

            Stopwatch watch = new Stopwatch();
            watch.Start();
            var query = entMan.AllEntityQueryEnumerator<TransformComponent, GunComponent, PointCannonComponent>();
            while (query.MoveNext(out var uid, out var form, out var gun, out var cannon))
            {
                if (form.ParentUid == gridUid)
                {
                    count++;
                    cannonSys.RefreshFiringRanges(uid, form, gun, cannon, number);
                }
            }

            shell.WriteLine($"Generated ranges for {count} cannons in {watch.Elapsed.TotalSeconds} seconds.");
        }
        else
        {
            shell.WriteError("Invalid grid uid.");
        }
    }
}
