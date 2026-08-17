using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._EE.Contractors.Components;

/// <summary>
/// Structured identity data printed in a passport. These values deliberately live on the
/// document instead of continuing to reference the character profile so the document can be
/// edited independently in-game.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PassportComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsClosed;

    [DataField, AutoNetworkedField]
    public string FullName = string.Empty;

    [DataField, AutoNetworkedField]
    public int Age;

    [DataField, AutoNetworkedField]
    public string Species = string.Empty;

    /// <summary>
    /// Species used by the portrait sprite. This is separate from the editable printed species
    /// so arbitrary player text cannot request a sprite state that does not exist.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string PortraitSpecies = "human";

    [DataField, AutoNetworkedField]
    public string Sex = string.Empty;

    [DataField, AutoNetworkedField]
    public int HeightCm;

    [DataField, AutoNetworkedField]
    public string SkinColor = string.Empty;

    [DataField, AutoNetworkedField]
    public string EyeColor = string.Empty;

    [DataField, AutoNetworkedField]
    public string Nationality = string.Empty;

    /// <summary>
    /// Reserved now so a future religion system can populate it without another passport data
    /// migration. Until then players may fill the printed field themselves.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Religion = string.Empty;

    [DataField, AutoNetworkedField]
    public string PassportId = string.Empty;

    [DataField, AutoNetworkedField]
    public int IssueYear;

    [DataField, AutoNetworkedField]
    public int ExpirationYear;

    /// <summary>
    /// Issuer-side authenticity marker. It is intentionally not exposed by the editing UI. A
    /// document issued while this is false is filled in like any other but never gains a
    /// registry record, so a checker machine finds nothing to print for it.
    /// </summary>
    [DataField]
    public bool Authentic = true;

    /// <summary>
    /// The issuing registry's copy of the identity this document was issued with. It is never
    /// networked to clients and never reachable from the editing UI, so a forger can change what
    /// the passport reads but not what the issuer recorded. A checker machine prints this copy
    /// verbatim and leaves the comparison to whoever is reading it.
    /// </summary>
    [DataField]
    public PassportRecord? Record;
}

/// <summary>
/// A frozen copy of the identity fields as the issuer recorded them. Deliberately separate from
/// <see cref="PassportComponent"/> so editing the document cannot touch it.
/// </summary>
[DataDefinition]
public sealed partial class PassportRecord
{
    [DataField]
    public string FullName = string.Empty;

    [DataField]
    public int Age;

    [DataField]
    public string Species = string.Empty;

    [DataField]
    public string Sex = string.Empty;

    [DataField]
    public int HeightCm;

    [DataField]
    public string SkinColor = string.Empty;

    [DataField]
    public string EyeColor = string.Empty;

    [DataField]
    public string Nationality = string.Empty;

    [DataField]
    public string PassportId = string.Empty;

    [DataField]
    public int IssueYear;

    [DataField]
    public int ExpirationYear;
}

/// <summary>
/// Marks a dispenser as a passport verifier. Structured passports are inspected without being
/// consumed; the dispenser's legacy prototype mappings remain available for old passports.
/// </summary>
[RegisterComponent]
public sealed partial class PassportCheckerComponent : Component;

[Serializable, NetSerializable]
public enum PassportUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class PassportBoundUserInterfaceState(
    string fullName,
    int age,
    string species,
    string sex,
    int heightCm,
    string skinColor,
    string eyeColor,
    string nationality,
    string religion,
    string passportId,
    int issueYear,
    int expirationYear) : BoundUserInterfaceState
{
    public string FullName { get; } = fullName;
    public int Age { get; } = age;
    public string Species { get; } = species;
    public string Sex { get; } = sex;
    public int HeightCm { get; } = heightCm;
    public string SkinColor { get; } = skinColor;
    public string EyeColor { get; } = eyeColor;
    public string Nationality { get; } = nationality;
    public string Religion { get; } = religion;
    public string PassportId { get; } = passportId;
    public int IssueYear { get; } = issueYear;
    public int ExpirationYear { get; } = expirationYear;
}

[Serializable, NetSerializable]
public sealed class PassportSaveMessage(
    string fullName,
    int age,
    string species,
    string sex,
    int heightCm,
    string skinColor,
    string eyeColor,
    string nationality,
    string religion,
    string passportId,
    int issueYear,
    int expirationYear) : BoundUserInterfaceMessage
{
    public string FullName { get; } = fullName;
    public int Age { get; } = age;
    public string Species { get; } = species;
    public string Sex { get; } = sex;
    public int HeightCm { get; } = heightCm;
    public string SkinColor { get; } = skinColor;
    public string EyeColor { get; } = eyeColor;
    public string Nationality { get; } = nationality;
    public string Religion { get; } = religion;
    public string PassportId { get; } = passportId;
    public int IssueYear { get; } = issueYear;
    public int ExpirationYear { get; } = expirationYear;
}
