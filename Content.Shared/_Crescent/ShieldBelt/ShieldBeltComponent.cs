namespace Content.Shared._Crescent.ShieldBelt;

/// <summary>
/// Marks a piece of clothing as a noospheric dampening emitter. Whenever its barrier is up the wearer is
/// psionically insulated, so the only way to reach them with a power is to drop the barrier first.
/// </summary>
/// <remarks>
/// The barrier itself is the ordinary clothing shield stack - ToggleClothing + ItemToggle + Blocking + Battery +
/// RechargeableBlocking - which is what makes it something you can simply shoot down, and what puts raising it
/// back up on the wearer's hotbar. This component only ties the insulation to whether that barrier is standing.
/// </remarks>
[RegisterComponent]
public sealed partial class ShieldBeltComponent : Component
{
    /// <summary>
    /// Who is wearing this, so the barrier knows whose noosphere it is muffling.
    /// </summary>
    [ViewVariables]
    public EntityUid? Wearer;

    /// <summary>
    /// Whether this belt is the reason the wearer is insulated right now. Tracked so we never strip insulation
    /// that something else - a tinfoil hat, a chem - is holding up.
    /// </summary>
    [ViewVariables]
    public bool Insulating;

    /// <summary>
    /// Whether the wearer can still reach out with their own powers through the barrier. The dampener is meant
    /// to stop powers landing on whoever is wearing it, not to gag a psion who straps one on, so this defaults
    /// to letting them cast out - unlike a tinfoil hat, which cuts both ways.
    /// </summary>
    [DataField]
    public bool Passthrough = true;
}
