using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.Factions;

/// <summary>
/// Marks a machine as belonging to a specific faction, so faction-gated machinery (research servers and the
/// lathes that draw on them) can tell whose hardware it is.
///
/// Machines mapped into a station normally don't carry this and fall back to their grid's IFF faction, which
/// is what makes a station's own equipment work together. Machines a player constructs get stamped with the
/// builder's faction instead, so dropping a microforge onto someone else's grid no longer grants access to
/// that grid's research.
/// </summary>
/// <remarks>
/// State is generated rather than left to the prototype: <see cref="FactionMachineSystem.SetFaction"/> stamps
/// <see cref="Faction"/> at runtime on machines a player builds, and without networked fields the client would
/// keep the component's defaults and disagree with the server about who owns the machine.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionMachineComponent : Component
{
    /// <summary>
    /// The owning faction. Empty means unaligned.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public string Faction = "";

    /// <summary>
    /// Whether <see cref="Faction"/> is authoritative. When true the grid fallback is skipped even if
    /// <see cref="Faction"/> is empty, so a machine built by someone with no faction stays unaligned rather
    /// than inheriting the faction of whatever grid it happens to be sitting on.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool Explicit = true;

    /// <summary>
    /// When true, only members of <see cref="Faction"/> may open this machine's UI. Used for the per-faction
    /// microforges in freeplay, where owning the hardware shouldn't mean everyone aboard can print from it.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool RestrictAccess;

    /// <summary>
    /// Whether umbrella/legacy faction aliases should be folded to their parent faction. Persistent territory
    /// consoles disable this so the four major powers retain their exact ownership IDs.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool NormalizeFaction = true;

    /// <summary>
    /// Popup shown when someone outside the faction tries to use a restricted machine.
    /// </summary>
    [DataField]
    public string? DeniedPopup = "faction-machine-access-denied";
}
