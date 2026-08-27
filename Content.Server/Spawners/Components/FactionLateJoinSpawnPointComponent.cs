using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Spawners.Components;

[RegisterComponent]
public sealed partial class FactionLateJoinSpawnPointComponent : Component
{
    [DataField("faction_id", required: true)]
    public ProtoId<FactionPrototype> Faction;

    /// <summary>
    /// False after this faction's conquest station falls. Disabled faction points deliberately remain present so
    /// the spawn selector does not fall back to a generic late-join point for the defeated faction.
    /// </summary>
    [ViewVariables]
    public bool Enabled = true;
}
