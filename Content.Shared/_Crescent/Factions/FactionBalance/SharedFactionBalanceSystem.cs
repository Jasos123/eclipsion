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
    /// <param name="inPlay">
    /// The factions this round can actually be joined as. An empty or null set means the caller does not
    /// know, and every faction is treated as present - the behaviour from before this was tracked.
    /// </param>
    public Dictionary<string, FactionBalanceEntry> CalculateCaps(
        IReadOnlyDictionary<string, int> counts,
        int baseSlots,
        int tolerance,
        IReadOnlySet<string>? inPlay = null)
    {
        var result = new Dictionary<string, FactionBalanceEntry>();

        // First pass: the whole tracked population, and whether the war group is in this round at all.
        var trackedTotal = 0;
        var parityPlayed = false;

        foreach (var faction in _prototype.EnumeratePrototypes<FactionPrototype>())
        {
            if (faction.BalanceMode == FactionBalanceMode.None)
                continue;

            trackedTotal += counts.GetValueOrDefault(faction.ID);

            if (faction.BalanceMode == FactionBalanceMode.Parity
                && faction.BalanceWeight > 0f
                && IsInPlay(faction, inPlay))
            {
                parityPlayed = true;
            }
        }

        // Second pass: the group that holds itself level, so an unpopular support faction cannot drag the
        // war factions' caps down. Factions outside it are measured against everyone instead.
        var groupTotal = 0;
        var groupWeight = 0f;

        foreach (var faction in _prototype.EnumeratePrototypes<FactionPrototype>())
        {
            if (!IsHeldLevel(faction, parityPlayed, inPlay))
                continue;

            groupTotal += counts.GetValueOrDefault(faction.ID);
            groupWeight += GetGroupWeight(faction);
        }

        foreach (var faction in _prototype.EnumeratePrototypes<FactionPrototype>())
        {
            if (faction.BalanceMode == FactionBalanceMode.None)
                continue;

            var count = counts.GetValueOrDefault(faction.ID);
            int cap;

            if (groupWeight > 0f && IsHeldLevel(faction, parityPlayed, inPlay))
            {
                // The +1 is the player asking to join: a faction may take the next slot only if it would
                // still be within its share afterwards. With two equal factions this caps the lead at one.
                cap = (int) MathF.Ceiling((groupTotal + 1) * GetGroupWeight(faction) / groupWeight);
            }
            else
            {
                cap = faction.BalanceMode == FactionBalanceMode.Share
                    ? (int) MathF.Floor(trackedTotal * faction.BalanceShare)
                    : 0;
            }

            result[faction.ID] = new FactionBalanceEntry(count, Math.Max(baseSlots, cap + tolerance));
        }

        return result;
    }

    /// <summary>
    /// Whether this faction belongs to the group that only measures itself. With the war factions in the
    /// round that group is the parity factions, exactly as before. With none of them in it - a round run
    /// between share factions alone, like Freeplay TFCF vs SHI - it is the factions that are in the round,
    /// because a share of a population only those same factions can grow is a deadlock: two factions on a
    /// quarter each never reach the headcount that would widen their own caps, so both jam at their base
    /// slots and the round stops accepting players while half the server sits in the lobby.
    /// </summary>
    private static bool IsHeldLevel(FactionPrototype faction, bool parityPlayed, IReadOnlySet<string>? inPlay)
    {
        if (faction.BalanceMode == FactionBalanceMode.None || GetGroupWeight(faction) <= 0f)
            return false;

        // A faction nobody can join never joins the group either: absent factions diluting the weights is
        // exactly what jams the caps in the first place.
        if (!IsInPlay(faction, inPlay))
            return false;

        return !parityPlayed || faction.BalanceMode == FactionBalanceMode.Parity;
    }

    /// <summary>
    /// Whether this faction has any way into it this round. Told from the jobs the round was set up with,
    /// not from the headcount: a war faction that happens to be empty for a moment is still the side the
    /// others are measured against, and one stray player must not decide how the whole server balances.
    /// </summary>
    private static bool IsInPlay(FactionPrototype faction, IReadOnlySet<string>? inPlay)
    {
        return inPlay is not { Count: > 0 } || inPlay.Contains(faction.ID);
    }

    /// <summary>
    /// Relative pull inside the level-held group. Parity factions bring their weight, share factions bring
    /// their share, so a faction allowed half as much of the server still ends up half the size.
    /// </summary>
    private static float GetGroupWeight(FactionPrototype faction)
    {
        return faction.BalanceMode == FactionBalanceMode.Parity ? faction.BalanceWeight : faction.BalanceShare;
    }

    #endregion
}
