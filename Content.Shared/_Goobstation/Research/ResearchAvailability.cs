using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.Research;

[Serializable, NetSerializable]
public enum ResearchAvailability : byte
{
    Researched,
    Available,
    PrereqsMet,
    Unavailable
}

public static class ResearchAvailabilityHelper
{
    /// <summary>
    /// Whether two availability maps describe the same thing. Both sides use this to skip redundant
    /// work - the server to decide a full state is not worth sending, the client to decide the cards
    /// do not need rebuilding - so the two have to agree on what "unchanged" means. Hence one copy.
    /// </summary>
    public static bool ResearchesEqual(
        IReadOnlyDictionary<string, ResearchAvailability> first,
        IReadOnlyDictionary<string, ResearchAvailability> second)
    {
        if (first.Count != second.Count)
            return false;

        foreach (var (id, availability) in first)
        {
            if (!second.TryGetValue(id, out var otherAvailability) || availability != otherAvailability)
                return false;
        }

        return true;
    }
}

