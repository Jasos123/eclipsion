using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Crescent.PlanetfallObjectives;

[AdminCommand(AdminFlags.Debug)]
public sealed class PlanetfallBarrierAnnouncerCommand : IConsoleCommand
{
    public string Command => "planetfall_releasebarrier";
    public string Description => "Immediately releases the Planetfall barrier.";
    public string Help => "In-game, releases the barrier on your current map. From the server console, releases the active Planetfall barrier.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        var announcerSystem = entityManager.System<PlanetfallBarrierAnnouncerSystem>();
        bool released;

        if (shell.Player?.AttachedEntity is { } playerEntity)
        {
            if (!entityManager.TryGetComponent<TransformComponent>(playerEntity, out var transform))
            {
                shell.WriteError("Attached entity has no transform.");
                return;
            }

            released = announcerSystem.TryReleaseOnMap(transform.MapID);
        }
        else
        {
            released = announcerSystem.TryReleaseAny();
        }

        if (!released)
        {
            shell.WriteError("No unreleased Planetfall barrier announcer was found.");
            return;
        }

        shell.WriteLine("Planetfall barrier release triggered.");
    }
}
