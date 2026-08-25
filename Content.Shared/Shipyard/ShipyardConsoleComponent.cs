using Content.Shared.Containers.ItemSlots;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Shipyard;

/// <summary>
/// Component for the shipyard console.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedShipyardConsoleSystem))]
public sealed partial class ShipyardConsoleComponent : Component
{
    /// <summary>
    /// Sound played when the ship can't be bought for any reason.
    /// </summary>
    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    /// <summary>
    /// Sound played when a ship is purchased.
    /// </summary>
    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>
    /// Radio channel to send the purchase announcement to.
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> Channel = "Command";

    /// <summary>
    /// Whether ships can be sold at this console. Defaults to true so every existing shipyard keeps
    /// working; set to false on LPC-only / empty consoles to prevent selling (and money exploits).
    /// </summary>
    [DataField]
    public bool CanSell = true;

    /// <summary>
    /// Whether purchases/sales at this console are paid from (and refunded to) the owning station's
    /// faction treasury instead of the buyer's personal bank account. Faction/mothership shipyards set
    /// this true; civilian shipyards leave it false so players buy ships with their own money, and the
    /// UI shows their personal balance. Only takes effect when the station actually has a faction.
    /// </summary>
    [DataField]
    public bool UsesFactionTreasury = false;

    // Eclipsion Start - high-value purchase approval
    /// <summary>
    /// Share of the owning faction's treasury above which a purchase has to be signed off at that
    /// faction's treasury console before it will go through. Expressed as a fraction of the balance
    /// rather than a flat price so the bar moves with how much the faction can actually afford.
    /// Zero disables approval entirely, which is what civilian yards want since they spend the buyer's
    /// own money; it only ever applies to consoles that bill the treasury.
    /// </summary>
    [DataField]
    public float ApprovalTreasuryFraction = 0.25f;
    // Eclipsion End

    /// Hullrot edit
    public static string TargetIdCardSlotId = "ShipyardConsole-targetId";

    [DataField("targetIdSlot")]
    public ItemSlot TargetIdSlot = new();


    [DataField("shipyardChannel")]
    public string ShipyardChannel = "CentCom";

    [DataField("securityShipyardChannel")]
    public string SecurityShipyardChannel = "CentCom";

    /// Hulllrot edit end

}
