using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipPower;

/// <summary>
/// Makes a ship weapon spend its own battery to fire. What the grid actually sees is the battery recharging
/// (ApcPowerReceiverBattery.batteryRechargeRate) - powerLoad on these is dead, PowerNetSystem overwrites it.
/// Server side only, so don't put this on anything predicted.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WeaponPowerDrawComponent : Component
{
    /// <summary>
    /// Joules off the battery per shot.
    /// </summary>
    [DataField(required: true)]
    public float EnergyPerShot;
}
