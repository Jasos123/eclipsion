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
