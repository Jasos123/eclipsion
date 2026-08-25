using Robust.Shared.Prototypes;

namespace Content.Shared._EE.Contractors.Prototypes;

/// <summary>
/// The binding a passport is printed in. Covers are deliberately kept separate from
/// <see cref="NationalityPrototype"/>: a document can be rebound in any polity's cover without
/// its issuer record following, which is what makes a forged cover worth catching by hand.
/// </summary>
[Prototype("passportCover")]
public sealed partial class PassportCoverPrototype : IPrototype
{
    [IdDataField, ViewVariables]
    public string ID { get; private set; } = string.Empty;

    /// <summary>
    /// Loc key for the name shown in the rebinding list.
    /// </summary>
    [DataField(required: true)]
    public string NameKey { get; private set; } = string.Empty;

    /// <summary>
    /// RSI state prefix. "_open" and "_closed" are appended for the two document states, so both
    /// must exist in the passport sprite's RSI.
    /// </summary>
    [DataField(required: true)]
    public string State { get; private set; } = string.Empty;

    /// <summary>
    /// Whether players may rebind a document into this cover. Legacy bindings that belong to no
    /// current polity stay defined so old documents still render, but are kept out of the list.
    /// </summary>
    [DataField]
    public bool Selectable { get; private set; } = true;

    /// <summary>
    /// Ordering in the rebinding list. Lower comes first.
    /// </summary>
    [DataField]
    public int Priority { get; private set; }
}
