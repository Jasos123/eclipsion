namespace Content.Shared._Crescent.PDA;

/// <summary>
/// Marks a cartridge program as owner-only: it can be opened and used by the player its host PDA is
/// bound to and nobody else. Put this on anything that spends money or reads private traffic.
/// </summary>
/// <remarks>
/// Programs are gated individually rather than the whole PDA UI so a looted device is still usable as
/// a light and a notepad, and so a new app has to opt in to being sensitive instead of accidentally
/// inheriting a lock.
/// </remarks>
[RegisterComponent]
public sealed partial class OwnerLockedProgramComponent : Component
{
}
