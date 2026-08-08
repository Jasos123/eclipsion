using System.Numerics;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Shuttles.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRadarConsoleSystem))]
public sealed partial class RadarConsoleComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float RangeVV
    {
        get => MaxRange;
        set => IoCManager
            .Resolve<IEntitySystemManager>()
            .GetEntitySystem<SharedRadarConsoleSystem>()
            .SetRange(Owner, value, this);
    }

    [DataField, AutoNetworkedField]
    public float MaxRange = 512f;
    ///changed maxrange from 256 to 512, does not affect bullet render range, only ship/targeting console range

    /// <summary>
    /// If true, the radar will be centered on the entity. If not - on the grid on which it is located.
    /// </summary>
    [DataField]
    public bool FollowEntity = false;

    // hullrot edit
    public NavBoundUserInterfaceState? LastUpdatedState = null;

    /// <summary>
    /// The next scheduled IFF refresh. This server-only value is staggered so consoles update on different ticks.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextIffUpdate = TimeSpan.Zero;

    [DataField]
    public bool KeepWorldAligned = false;
    // end

    // Frontier: settable target
    [DataField]
    public Vector2? Target;

    [DataField]
    public EntityUid? TargetEntity;

    [DataField]
    public bool HideTarget = false;
}
