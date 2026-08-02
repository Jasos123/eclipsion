using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Taxation;

/// <summary>
/// A single taxable trade good as shown on the taxation console.
/// </summary>
[Serializable, NetSerializable]
public struct TaxableGoodEntry
{
    /// <summary>Trade good prototype id (the item you feed into a trade point).</summary>
    public string ProtoId;

    /// <summary>Human readable name for display.</summary>
    public string Name;

    /// <summary>The base (sticker) sale value of the good at this station's trade points.</summary>
    public int BasePrice;

    /// <summary>The effective tax rate applied to this good (0..1).</summary>
    public float EffectiveRate;

    /// <summary>Whether this good has a per-good override (true) or inherits the default (false).</summary>
    public bool HasOverride;
}

[Serializable, NetSerializable]
public sealed class TaxationConsoleState : BoundUserInterfaceState
{
    /// <summary>Station-wide default tax rate (0..1).</summary>
    public float DefaultRate;

    /// <summary>Upper bound any rate is clamped to (0..1).</summary>
    public float MaxRate;

    /// <summary>Current treasury balance for reference.</summary>
    public int Treasury;

    /// <summary>Whether the interacting player is allowed to change rates.</summary>
    public bool CanEdit;

    /// <summary>All trade goods sold through this station's trade points.</summary>
    public List<TaxableGoodEntry> Goods;

    public TaxationConsoleState(float defaultRate, float maxRate, int treasury, bool canEdit, List<TaxableGoodEntry> goods)
    {
        DefaultRate = defaultRate;
        MaxRate = maxRate;
        Treasury = treasury;
        CanEdit = canEdit;
        Goods = goods;
    }
}

/// <summary>Sets the station-wide default tax rate. Rate is a fraction (0..1).</summary>
[Serializable, NetSerializable]
public sealed class TaxationSetDefaultRateMessage : BoundUserInterfaceMessage
{
    public float Rate;

    public TaxationSetDefaultRateMessage(float rate)
    {
        Rate = rate;
    }
}

/// <summary>Sets a per-good tax override. Rate is a fraction (0..1).</summary>
[Serializable, NetSerializable]
public sealed class TaxationSetOverrideMessage : BoundUserInterfaceMessage
{
    public string ProtoId;
    public float Rate;

    public TaxationSetOverrideMessage(string protoId, float rate)
    {
        ProtoId = protoId;
        Rate = rate;
    }
}

/// <summary>Removes a per-good override so the good falls back to the default rate.</summary>
[Serializable, NetSerializable]
public sealed class TaxationClearOverrideMessage : BoundUserInterfaceMessage
{
    public string ProtoId;

    public TaxationClearOverrideMessage(string protoId)
    {
        ProtoId = protoId;
    }
}

[Serializable, NetSerializable]
public enum TaxationConsoleUiKey : byte
{
    Key,
}

// ---------------------------------------------------------------------------
// Faction treasury console
// ---------------------------------------------------------------------------

[Serializable, NetSerializable]
public sealed class TreasuryConsoleState : BoundUserInterfaceState
{
    /// <summary>Current treasury balance.</summary>
    public int Balance;

    /// <summary>Whether the interacting player is authorized (has faction funds access).</summary>
    public bool Authorized;

    /// <summary>Whether a security breach is currently active on this console.</summary>
    public bool AlarmActive;

    /// <summary>
    /// How much of <see cref="Balance"/> this viewer may still draw this round, after their per-person
    /// share. Shown because "Withdraw All" used to ask for the whole balance, get silently clamped to
    /// the cap, and report a smaller figure with no explanation of where the rest went.
    /// </summary>
    public int Remaining;

    /// <summary>The per-person share of the vault, as a percentage, for the cap explanation.</summary>
    public int CapPercent;

    public TreasuryConsoleState(int balance, bool authorized, bool alarmActive, int remaining, int capPercent)
    {
        Balance = balance;
        Authorized = authorized;
        AlarmActive = alarmActive;
        Remaining = remaining;
        CapPercent = capPercent;
    }
}

/// <summary>Withdraws the given amount of credits from the treasury as physical cash.</summary>
[Serializable, NetSerializable]
public sealed class TreasuryWithdrawMessage : BoundUserInterfaceMessage
{
    public int Amount;

    public TreasuryWithdrawMessage(int amount)
    {
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public enum TreasuryConsoleUiKey : byte
{
    Key,
}
