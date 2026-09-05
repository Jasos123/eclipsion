namespace Content.Server._Crescent.Payphone;

/// <summary>
/// Records that a character has started the call, even if it was interrupted.
/// Stored on the mind when available so changing bodies or phones cannot replay it.
/// </summary>
[RegisterComponent]
public sealed partial class PayphoneMemoryComponent : Component;
