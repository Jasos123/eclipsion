using Robust.Shared.Network;

namespace Content.Shared._Crescent.PDA;

/// <summary>
/// Binds a PDA to the first player who opens it, so killing someone and taking their PDA does not hand
/// the killer their bank, their stock portfolio or their private messages.
/// </summary>
/// <remarks>
/// The lock is on the player, not the ID card: a stolen PDA with the victim's ID still in it stays
/// locked, and the owner keeps their PDA through cloning or a body transfer because the binding is to
/// the mind's user rather than to a body. Only programs marked <see cref="OwnerLockedProgramComponent"/>
/// are gated - the flashlight, ringtone and notepad keep working for anyone, so a looted PDA is still
/// a torch.
/// </remarks>
[RegisterComponent]
public sealed partial class PdaLockComponent : Component
{
    /// <summary>
    /// The player this device answers to, captured the first time someone opens it. Null means the PDA
    /// is fresh out of a locker and will bind to whoever opens it next.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public NetUserId? OwnerUser;

    /// <summary>The owner's character name at binding time, shown in the refusal message.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? OwnerName;

    /// <summary>
    /// Set false on a PDA that should never bind (debug PDAs, admin gear, prototypes handed out to
    /// whole departments).
    /// </summary>
    [DataField]
    public bool Enabled = true;
}
