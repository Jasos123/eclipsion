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
    /// Issuer-side authenticity marker. It is intentionally not exposed by the editing UI.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Authentic = true;

    /// <summary>
    /// Set when a player saves changes through the passport editor. Initial issuer/profile data
    /// does not count as tampering.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Tampered;

    /// <summary>
    /// Server-side biometric reference recorded when the passport is issued. This is a hash of
    /// the holder's fingerprint rather than the raw forensic identifier, and is deliberately not
    /// networked to clients or exposed by the passport editing UI.
    /// </summary>
    [DataField]
    public string BiometricHash = string.Empty;
}

/// <summary>
/// Marks a dispenser as a passport verifier. Structured passports are inspected without being
/// consumed; the dispenser's legacy prototype mappings remain available for old passports.
/// </summary>
[RegisterComponent]
public sealed partial class PassportCheckerComponent : Component;

/// <summary>
/// Raised after a passport has been populated for a character so the server can bind its
/// machine-readable biometric reference to the holder.
/// </summary>
[ByRefEvent]
public readonly record struct PassportIssuedEvent(EntityUid Holder);

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
    int expirationYear,
    bool isValid,
    bool tampered) : BoundUserInterfaceState
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
    public bool IsValid { get; } = isValid;
    public bool Tampered { get; } = tampered;
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
