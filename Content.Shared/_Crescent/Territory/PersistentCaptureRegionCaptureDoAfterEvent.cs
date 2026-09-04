using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.Territory;

/// <summary>
/// Raised when a player finishes or interrupts a manual persistent-territory capture stage.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class PersistentCaptureRegionCaptureDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public string Team = string.Empty;

    [DataField]
    public string? ExpectedOwner;

    public PersistentCaptureRegionCaptureDoAfterEvent()
    {
    }

    public PersistentCaptureRegionCaptureDoAfterEvent(string team, string? expectedOwner)
    {
        Team = team;
        ExpectedOwner = expectedOwner;
    }
}
