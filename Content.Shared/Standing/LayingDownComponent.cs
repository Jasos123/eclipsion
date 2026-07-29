using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Standing;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LayingDownComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan StandingUpTime = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public float LyingSpeedModifier = 0.35f,
                 CrawlingUnderSpeedModifier = 0.5f;

    /// <summary>
    ///     Chance for a projectile or hitscan fired by a gun to pass over this entity while they are lying down.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float GunshotMissChance = 0.35f;

    /// <summary>
    ///     Multiplier applied to gun spread while this entity is lying down.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float GunSpreadModifier = 0.65f;

    [DataField, AutoNetworkedField]
    public bool AutoGetUp;

    /// <summary>
    ///     If true, the entity is choosing to crawl under furniture. This is purely visual and has no effect on physics.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsCrawlingUnder = false;

    [DataField, AutoNetworkedField]
    public int NormalDrawDepth = (int) DrawDepth.DrawDepth.Mobs,
               CrawlingUnderDrawDepth = (int) DrawDepth.DrawDepth.SmallMobs;
}

[Serializable, NetSerializable]
public sealed class ChangeLayingDownEvent : CancellableEntityEventArgs;

[Serializable, NetSerializable]
public sealed class CheckAutoGetUpEvent(NetEntity user) : CancellableEntityEventArgs
{
    public NetEntity User = user;
}
