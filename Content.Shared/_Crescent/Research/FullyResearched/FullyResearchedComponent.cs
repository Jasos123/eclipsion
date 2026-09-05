using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Research.FullyResearched;

/// <summary>
/// Put on a research server to hand it its entire tree at map init, already unlocked.
/// </summary>
/// <remarks>
/// This is a mapping convenience, not a gameplay mechanic: it exists so a map can ship a station whose R&D is
/// already done, without anyone having to sit at a console. It reads the database's own supportedDisciplines,
/// so a faction server unlocks that faction's catalogue and nothing else. Hidden technologies stay hidden.
/// </remarks>
[RegisterComponent]
public sealed partial class FullyResearchedComponent : Component
{
    /// <summary>
    /// Whether the server also keeps a stock of research points afterwards. Purely for flavour on consoles --
    /// there is nothing left to spend them on.
    /// </summary>
    [DataField]
    public int Points;
}
