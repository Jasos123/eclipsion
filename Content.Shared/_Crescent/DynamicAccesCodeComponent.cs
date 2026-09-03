using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DynamicCodeHolderComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<int> codes = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, HashSet<int>> mappedCodes = new();

    /// <summary>
    ///     Whether this holder's keys are currently counted against the server's key refcount.
    /// </summary>
    /// <remarks>
    ///     Holders are routinely built detached - keys are pushed into a bare component and only then is it
    ///     added to an entity - so the count cannot be taken at the time the key is handed over. This says
    ///     which side of that line the holder is on: while it is false the keys are the caller's to hand out
    ///     freely, and ComponentInit counts them all exactly once. Server bookkeeping only, never networked.
    /// </remarks>
    [ViewVariables]
    public bool counted;

    /// <summary>
    ///     On a grid, the names its captain's and pilot's keys were filed under in <see cref="mappedCodes"/>.
    /// </summary>
    /// <remarks>
    ///     A ship is keyed once, when its map initialises, and every shuttle console standing on it at that
    ///     moment is told these two names. A console that arrives later - one a crewman builds, or one the
    ///     drydock welds back on after the original was shot out - misses that pass entirely, and with no
    ///     names to look up it can never match a swiped ID against anything. Keeping them on the grid lets
    ///     such a console adopt them the moment it is stood up. Server bookkeeping only, never networked.
    /// </remarks>
    [ViewVariables]
    public string? captainIdentifier;

    /// <inheritdoc cref="captainIdentifier"/>
    [ViewVariables]
    public string? pilotIdentifier;

    /// <summary>
    ///     On a grid, the access mapping its keys were cut from.
    /// </summary>
    /// <remarks>
    ///     The mapping says which keys a door, a console or a locker is entitled to. It is consulted once, as
    ///     the ship initialises, and the initializer component is then dropped - so a door built or rebuilt
    ///     afterwards has nothing left to consult and falls back to whatever static access its prototype
    ///     carries, quietly opening to the wrong people. Keeping the name here lets it be cut a key later.
    ///     Server bookkeeping only, never networked.
    /// </remarks>
    [ViewVariables]
    public string? accesMapping;
}

