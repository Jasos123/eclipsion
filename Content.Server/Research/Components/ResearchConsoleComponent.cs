using Content.Shared.Radio;
using Content.Shared.Research.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Components;

[RegisterComponent]
public sealed partial class ResearchConsoleComponent : Component
{
    /// <summary>
    /// The radio channel that the unlock announcements are broadcast to.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<RadioChannelPrototype> AnnouncementChannel = "Science";

    /// <summary>
    /// Last complete tree state sent to viewers. Point-only changes use a small BUI
    /// message while the availability map is unchanged.
    /// </summary>
    public ResearchConsoleBoundInterfaceState? LastUiState;
}

