using Content.Shared.DeltaV.CartridgeLoader.Cartridges;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.DeltaV.NanoChat;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedNanoChatSystem))]
[AutoGenerateComponentPause, AutoGenerateComponentState]
public sealed partial class NanoChatCardComponent : Component
{
    /// <summary>
    ///     The number assigned to this card.
    /// </summary>
    [DataField, AutoNetworkedField]
    public uint? Number;

    /// <summary>
    ///     All chat recipients stored on this card.
    /// </summary>
    [DataField]
    public Dictionary<uint, NanoChatRecipient> Recipients = new();

    /// <summary>
    ///     All messages stored on this card, keyed by recipient number.
    /// </summary>
    [DataField]
    public Dictionary<uint, List<NanoChatMessage>> Messages = new();

    /// <summary>
    ///     The currently selected chat recipient number.
    /// </summary>
    [DataField]
    public uint? CurrentChat;

    /// <summary>
    ///     The maximum amount of recipients this card supports.
    /// </summary>
    [DataField]
    public int MaxRecipients = 50;

    /// <summary>
    ///     Last time a message was sent, for rate limiting.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastMessageTime; // TODO: actually use this, compare against actor and not the card

    /// <summary>
    ///     Whether to send notifications.
    /// </summary>
    [DataField]
    public bool NotificationsMuted;

    // Eclipsion Start - blocking
    /// <summary>
    ///     NanoChat numbers this card refuses traffic from. A blocked sender's messages are rejected at
    ///     delivery and reported back to them as blocked, rather than silently vanishing.
    /// </summary>
    /// <remarks>
    ///     Blocks live on the card rather than the body because the card is the NanoChat identity: it is
    ///     what carries the number, and a card moved to a new PDA should keep its owner's block list.
    /// </remarks>
    [DataField]
    public HashSet<uint> BlockedNumbers = new();
    // Eclipsion End

    /// <summary>
    ///     The PDA that this card is currently inserted to.
    /// </summary>
    [DataField]
    public EntityUid? PdaUid = null;
    ///     Whether the card's number should be listed in NanoChat's lookup
    /// </summary>
    [DataField]
    public bool ListNumber = true;
}
