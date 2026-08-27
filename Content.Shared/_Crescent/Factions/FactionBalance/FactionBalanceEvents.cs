using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Factions.FactionBalance;

/// <summary>
/// Server -> client snapshot of the population-scaled join caps, so the late-join window can grey out
/// the jobs a player would be refused instead of letting them press the button and get nothing.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionBalanceStateEvent : EntityEventArgs
{
    /// <summary>
    /// Whether the caps are being enforced at all. False means every entry here is advisory.
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// Whether admins are exempt from the caps. Replicated because the CVar behind it is server-only,
    /// so the client cannot otherwise tell whether to grey a job out for an admin the server would admit.
    /// </summary>
    public bool AdminBypass;

    /// <summary>
    /// Faction prototype ID -> its current headcount and cap. Factions not taking part are absent.
    /// </summary>
    public Dictionary<string, FactionBalanceEntry> Factions;

    public FactionBalanceStateEvent(bool enabled, bool adminBypass, Dictionary<string, FactionBalanceEntry> factions)
    {
        Enabled = enabled;
        AdminBypass = adminBypass;
        Factions = factions;
    }
}

/// <summary>
/// One faction's standing in the balance: how many players it currently holds, and how many it may hold.
/// </summary>
[Serializable, NetSerializable]
public struct FactionBalanceEntry
{
    /// <summary>
    /// Players currently attached to a body belonging to this faction.
    /// </summary>
    public int Count;

    /// <summary>
    /// Headcount this faction is allowed to reach. A faction at or above its cap accepts nobody new.
    /// </summary>
    public int Cap;

    /// <summary>
    /// This faction's conquest station has fallen, permanently closing its jobs for the rest of the round.
    /// </summary>
    public bool Defeated;

    public FactionBalanceEntry(int count, int cap, bool defeated = false)
    {
        Count = count;
        Cap = cap;
        Defeated = defeated;
    }

    public readonly bool Full => Defeated || Count >= Cap;
}
