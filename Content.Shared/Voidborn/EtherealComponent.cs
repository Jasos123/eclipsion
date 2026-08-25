using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;


namespace Content.Shared.Voidborn;

[RegisterComponent, NetworkedComponent]
public sealed partial class EtherealComponent : Component
{
    /// <summary>
    ///     Does the Ent, Dark lights around it?
    /// </summary>
    [DataField]
    public bool Darken = false;

    /// <summary>
    ///     Range of the Darken Effect.
    /// </summary>
    [DataField]
    public float DarkenRange = 5;

    /// <summary>
    ///     Darken Effect Rate.
    /// </summary>
    [DataField]
    public float DarkenRate = 0.084f;

    /// Can this be stunned by ethereal stun objects?
    [DataField]
    public bool CanBeStunned = true;

    /// <summary>
    ///     How long the shadow state holds before it unwinds on its own. Phasing through walls with
    ///     no clock on it made DarkSwap a place to live rather than a move to make.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Filled in at map init from <see cref="Duration"/>.
    /// </summary>
    [DataField]
    public TimeSpan ExpiresAt;

    /// <summary>
    ///     How much stamina damage does the user take each second they are in the dark realm?
    /// </summary>
    [DataField]
    public float StaminaPerSecond = 1;

    [DataField]
    public float StaminaDamageOnFlash = 200f;

    public List<EntityUid> DarkenedLights = new();

    public float DarkenAccumulator;

    public int OldMobMask;

    public int OldMobLayer;

    public List<ProtoId<NpcFactionPrototype>> SuppressedFactions = new();
    public bool HasDoorBumpTag;
}
