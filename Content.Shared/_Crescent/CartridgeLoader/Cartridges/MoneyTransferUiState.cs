using Content.Shared.CartridgeLoader;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class MoneyTransferRecipientState
{
    public NetEntity Entity;
    public string Name;
    public string Job;

    /// <summary>Whether this person is on the viewer's block list. Eclipsion - blocking.</summary>
    public bool Blocked;

    public MoneyTransferRecipientState(NetEntity entity, string name, string job, bool blocked = false)
    {
        Entity = entity;
        Name = name;
        Job = job;
        Blocked = blocked;
    }
}

// Eclipsion Start - blocking
/// <summary>
/// One entry on the viewer's block list. Carries the account id rather than an entity so somebody who
/// has logged out, died or been gibbed can still be unblocked.
/// </summary>
[Serializable, NetSerializable]
public sealed class MoneyTransferBlockedState
{
    public NetUserId User;
    public string Name;

    public MoneyTransferBlockedState(NetUserId user, string name)
    {
        User = user;
        Name = name;
    }
}
// Eclipsion End

[Serializable, NetSerializable]
public sealed class MoneyTransferHistoryEntryState
{
    public bool Outgoing;
    public string Counterparty;
    public int Amount;
    public string Comment;
    public string TimeText;

    public MoneyTransferHistoryEntryState(bool outgoing, string counterparty, int amount, string comment, string timeText)
    {
        Outgoing = outgoing;
        Counterparty = counterparty;
        Amount = amount;
        Comment = comment;
        TimeText = timeText;
    }
}

[Serializable, NetSerializable]
public sealed class MoneyTransferUiState : BoundUserInterfaceState
{
    public long Balance;
    public List<MoneyTransferRecipientState> Recipients;
    public List<MoneyTransferHistoryEntryState> History;

    /// <summary>Everyone this device's owner refuses money and comments from. Eclipsion - blocking.</summary>
    public List<MoneyTransferBlockedState> Blocked;

    public string? Error;
    /// <summary>Shown once after a successful outgoing transfer (green toast on client).</summary>
    public string? Success;

    public MoneyTransferUiState(
        long balance,
        List<MoneyTransferRecipientState> recipients,
        List<MoneyTransferHistoryEntryState> history,
        string? error,
        string? success = null,
        List<MoneyTransferBlockedState>? blocked = null)
    {
        Balance = balance;
        Recipients = recipients;
        History = history;
        Error = error;
        Success = success;
        Blocked = blocked ?? new List<MoneyTransferBlockedState>(); // Eclipsion - blocking
    }
}

// Eclipsion Start - blocking
/// <summary>
/// Blocks or unblocks another player from sending this device's owner money (and the comment that rides
/// along with it). Targets a live entity because that is what the app can see; the server resolves it to
/// the account behind it so the block survives the body.
/// </summary>
[Serializable, NetSerializable]
public sealed class MoneyTransferBlockUiMessageEvent : CartridgeMessageEvent
{
    public NetEntity Target;
    public NetUserId? User;
    public bool Block;

    public MoneyTransferBlockUiMessageEvent(NetEntity target, bool block, NetUserId? user = null)
    {
        Target = target;
        Block = block;
        User = user;
    }
}
// Eclipsion End

[Serializable, NetSerializable]
public sealed class MoneyTransferUiMessageEvent : CartridgeMessageEvent
{
    public NetEntity Recipient;
    public int Amount;
    public string Comment;

    public MoneyTransferUiMessageEvent(NetEntity recipient, int amount, string comment)
    {
        Recipient = recipient;
        Amount = amount;
        Comment = comment;
    }
}
