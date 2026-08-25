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

}

