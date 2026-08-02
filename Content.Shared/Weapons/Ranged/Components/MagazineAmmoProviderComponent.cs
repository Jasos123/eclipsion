using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged;

/// <summary>
/// Wrapper around a magazine (handled via ItemSlot). Passes all AmmoProvider logic onto it.
/// </summary>
[RegisterComponent, Virtual]
public partial class MagazineAmmoProviderComponent : AmmoProviderComponent
{
    /// <summary>
    /// Seconds the gun is locked out for after a magazine goes in. Zero means it can fire immediately.
    /// </summary>
    [DataField]
    public float ReloadDuration;
}
