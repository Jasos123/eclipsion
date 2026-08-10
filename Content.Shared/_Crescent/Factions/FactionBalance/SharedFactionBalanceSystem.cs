using Content.Shared.Customization.Systems;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Crescent.Factions.FactionBalance;

/// <summary>
/// Shared half of the population-scaled spawn tickets: works out which faction a job belongs to, and
/// what headcount each faction is allowed at a given population. The server owns the live counts and
/// does the refusing; the client runs the same maths on a replicated snapshot to grey out buttons.
/// </summary>
public abstract class SharedFactionBalanceSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    /// <summary>
    /// Job prototype ID -> owning faction. Built once from the job requirements, which are the only
    /// job-to-faction link the client can see (<see cref="JobPrototype.Special"/> is server-only).
    /// </summary>
    private readonly Dictionary<string, string> _jobFactions = new();

    private bool _jobFactionsBuilt;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<JobPrototype>() || ev.WasModified<FactionPrototype>())
            _jobFactionsBuilt = false;
    }

    #region Job -> faction

    /// <summary>
    /// Finds the faction a job locks the player into, if any. Jobs without a faction requirement are
    /// unaligned and never capped.
    /// </summary>
    public bool TryGetJobFaction(string jobId, out string faction)
    {
        EnsureJobFactions();
        return _jobFactions.TryGetValue(jobId, out faction!);
    }

    /// <summary>
    /// All jobs belonging to the given faction.
    /// </summary>
    public IEnumerable<string> GetFactionJobs(string faction)
    {
        EnsureJobFactions();
        foreach (var (jobId, jobFaction) in _jobFactions)
        {
            if (jobFaction == faction)
                yield return jobId;
        }
    }

    private void EnsureJobFactions()
    {
        if (_jobFactionsBuilt)
            return;

        _jobFactionsBuilt = true;
        _jobFactions.Clear();

        foreach (var job in _prototype.EnumeratePrototypes<JobPrototype>())
        {
            if (job.Requirements is not { } requirements)
                continue;

            if (FindFaction(requirements) is { } faction)
                _jobFactions[job.ID] = faction;
        }
    }

    /// <summary>
    /// Walks a requirement tree for the faction the job is pinned to. Inverted requirements say what the
    /// player must not be, which pins nothing, so they are skipped.
    /// </summary>
    private static string? FindFaction(List<CharacterRequirement> requirements)
    {
        foreach (var requirement in requirements)
        {
            switch (requirement)
            {
                case FactionRequirement { Inverted: false } faction when faction.FactionID != string.Empty:
                    return faction.FactionID;
                case CharacterLogicRequirement logic when FindFaction(logic.Requirements) is { } nested:
                    return nested;
            }
        }

        return null;
    }

    #endregion

    #region Caps

    /// <summary>
    /// Works out every tracked faction's cap from the current headcounts.
    /// </summary>
    /// <param name="counts">Live headcount per faction. Factions absent from this are treated as empty.</param>
    /// <param name="baseSlots">Headcount every faction may always reach, so an empty server is joinable.</param>
    /// <param name="tolerance">Extra headroom on top of the computed share.</param>
    public Dictionary<string, FactionBalanceEntry> CalculateCaps(
        IReadOnlyDictionary<string, int> counts,
        int baseSlots,
        int tolerance)
    {
        var result = new Dictionary<string, FactionBalanceEntry>();

        // Two passes: the parity group only measures itself, so an unpopular support faction cannot drag
        // the war factions' caps down, while share factions are measured against everyone.
        var parityTotal = 0;
        var parityWeight = 0f;
        var trackedTotal = 0;

        foreach (var faction in _prototype.EnumeratePrototypes<FactionPrototype>())
        {
            if (faction.BalanceMode == FactionBalanceMode.None)
                continue;

            var count = counts.TryGetValue(faction.ID, out var c) ? c : 0;
            trackedTotal += count;

            if (faction.BalanceMode != FactionBalanceMode.Parity || faction.BalanceWeight <= 0f)
                continue;

            parityTotal += count;
            parityWeight += faction.BalanceWeight;
        }

        foreach (var faction in _prototype.EnumeratePrototypes<FactionPrototype>())
        {
            if (faction.BalanceMode == FactionBalanceMode.None)
                continue;

            var count = counts.TryGetValue(faction.ID, out var c) ? c : 0;
            var cap = faction.BalanceMode switch
            {
                // The +1 is the player asking to join: a faction may take the next slot only if it would
                // still be within its share afterwards. With two equal factions this caps the lead at one.
                FactionBalanceMode.Parity when faction.BalanceWeight > 0f && parityWeight > 0f =>
                    (int) MathF.Ceiling((parityTotal + 1) * faction.BalanceWeight / parityWeight),
                FactionBalanceMode.Share =>
                    (int) MathF.Floor(trackedTotal * faction.BalanceShare),
                _ => 0,
            };

            result[faction.ID] = new FactionBalanceEntry(count, Math.Max(baseSlots, cap + tolerance));
        }

        return result;
    }

    #endregion
}
