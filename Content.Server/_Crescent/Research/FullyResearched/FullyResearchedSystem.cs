using Content.Server.Research.Systems;
using Content.Shared._Crescent.Research.FullyResearched;
using Content.Shared.Research.Components;

namespace Content.Server._Crescent.Research.FullyResearched;

/// <summary>
/// Unlocks a research server's whole tree at map init. See <see cref="FullyResearchedComponent"/>.
/// </summary>
public sealed class FullyResearchedSystem : EntitySystem
{
    [Dependency] private readonly ResearchSystem _research = default!;

    /// <summary>
    /// A pass only unlocks what is reachable right now, and unlocking raises both the discipline tier and the
    /// prerequisites of the next batch, so the sweep repeats. The cap is a runaway guard; real trees settle in
    /// well under ten passes.
    /// </summary>
    private const int MaxPasses = 64;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FullyResearchedComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<FullyResearchedComponent> ent, ref MapInitEvent args)
    {
        UnlockEverything(ent);
    }

    /// <summary>
    /// Unlocks every non-hidden technology the database's disciplines can reach, then pushes the result to
    /// anything already listening to this server.
    /// </summary>
    public void UnlockEverything(Entity<FullyResearchedComponent> ent, TechnologyDatabaseComponent? database = null)
    {
        if (!Resolve(ent, ref database, false))
            return;

        var passes = 0;
        while (passes++ < MaxPasses)
        {
            var available = _research.GetAvailableTechnologies(ent, database);
            if (available.Count == 0)
                break;

            foreach (var tech in available)
            {
                _research.AddTechnology(ent, tech, database);
            }
        }

        _research.UpdateTechnologyCards(ent, database);

        if (!TryComp<ResearchServerComponent>(ent, out var server))
            return;

        if (ent.Comp.Points > 0)
            _research.ModifyServerPoints(ent, ent.Comp.Points, server);

        // Consoles and lathes copy the database when they register, and a client that registered before this ran
        // is holding the empty version. Push it again rather than making anyone re-select the server.
        foreach (var client in server.Clients)
        {
            _research.Sync(client, ent);
        }
    }
}
