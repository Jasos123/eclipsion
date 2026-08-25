using Content.Server.Shuttles.Systems;
using Content.Server._Crescent.Shuttles.Systems;
using Content.Shared._Crescent.Shuttles.Components;
using Robust.Shared.GameStates;

namespace Content.Server._Crescent.Shuttles.Components;

[RegisterComponent, Access(typeof(MassCloakConsoleSystem), typeof(ShuttleSystem))]
public sealed partial class MassCloakConsoleComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("massCloakEnabled")]
    public bool MassCloakEnabled = false;

    [ViewVariables(VVAccess.ReadWrite), DataField("massCloakRange")]
    public float MassCloakRange = 20f;

    /// <summary>
    /// Whether asteroid grids should be excluded from this cloak field.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool IgnoreAsteroids = true;

    /// <summary>
    /// Whether this field only operates while the grid carrying the console is IFF-cloaked.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool RequiresMothershipCloak;

    /// <summary>
    /// Whether this field may cloak the grid carrying the console.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool CloakMothership = true;

    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? originalGrid = null;

    /// <summary>
    /// List of grids currently being cloaked by this console
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> CloakedGrids = new();

    public const float MassCloakMinRange = 20f;
    public const float MassCloakMaxRange = 500f;
}
