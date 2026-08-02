namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>
/// Raised on the weapon before it regenerates a round. Set Allowed if you can pay for it.
/// </summary>
[ByRefEvent]
public record struct RechargeBasicEntityAmmoAttemptEvent(float EnergyPerCharge, bool Allowed = false);
