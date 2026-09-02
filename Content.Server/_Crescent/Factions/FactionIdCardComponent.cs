using Robust.Shared.GameObjects;

namespace Content.Server._Crescent.Factions;

/// <summary>
///     The faction advertised by an ID card. This is deliberately stored on the card rather than its holder:
///     anti-boarder defences authenticate the credential being worn, including stolen credentials.
/// </summary>
[RegisterComponent, Access(typeof(FactionIdCardSystem))]
public sealed partial class FactionIdCardComponent : Component
{
    [DataField]
    public string Faction = string.Empty;
}

/// <summary>
/// Remembers the exact physical faction cards issued to a member so dismissal can revoke them even after the member
/// moves them out of their ID slot.
/// </summary>
[RegisterComponent, Access(typeof(FactionIdCardSystem), typeof(FactionRecruitmentConsoleSystem))]
public sealed partial class FactionCredentialTrackerComponent : Component
{
    public readonly HashSet<EntityUid> Cards = new();
    public string Faction = string.Empty;
}
