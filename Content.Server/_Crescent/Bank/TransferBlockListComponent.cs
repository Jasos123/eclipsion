using Robust.Shared.Network;

namespace Content.Server._Crescent.Bank;

/// <summary>
/// The people this mob refuses PDA transfers from.
/// </summary>
/// <remarks>
/// A transfer carries a comment and pops a notification on the recipient's screen, so anyone willing to
/// spend a single credit could talk at someone who wanted nothing to do with them, repeatedly. Blocking
/// refuses the transfer outright rather than swallowing it, so nobody's money disappears into a block.
///
/// Keyed by account rather than by body: dying, cloning or changing ID does not quietly clear a block,
/// and the stored name is only there so the block list is readable when the person is offline.
/// </remarks>
[RegisterComponent]
public sealed partial class TransferBlockListComponent : Component
{
    /// <summary>Blocked players, mapped to the name they last went by for display.</summary>
    public Dictionary<NetUserId, string> Blocked = new();
}
