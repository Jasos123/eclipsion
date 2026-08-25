using Robust.Shared.Serialization;

namespace Content.Shared.Crescent.Radar;

[Serializable, NetSerializable]
public sealed class IFFInterfaceState
{
    public List<ProjectileState> Projectiles;
    public Dictionary<NetEntity, List<TurretState>> Turrets;

    /// <summary>
    ///     KS14: true while anything on the console's grid has taken damage recently, for the
    ///         console's TAKING FIRE warning. A settable field rather than a constructor
    ///         parameter so existing callers are untouched.
    ///     Rides the IFF state because that is the only part of the console state rebuilt
    ///         every UI tick; on the base state it would only refresh on a full push and the
    ///         warning would lag the shooting badly.
    /// </summary>
    public bool TakingFire;

    public IFFInterfaceState(List<ProjectileState> projectiles, Dictionary<NetEntity, List<TurretState>> turrets)
    {
        Projectiles = projectiles;
        Turrets = turrets;
    }
}
