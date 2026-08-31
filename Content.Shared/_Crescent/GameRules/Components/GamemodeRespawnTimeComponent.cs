using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Crescent.GameRules.Components;

/// <summary>
/// Added to a game rule entity to override the ghost respawn timer for that gamemode.
/// </summary>
[RegisterComponent]
public sealed partial class GamemodeRespawnTimeComponent : Component
{
    /// <summary>
    /// How many minutes after death until a player can respawn. Overrides ghost.respawn_time CVar.
    /// </summary>
    [DataField]
    public float RespawnTimeMinutes = 5f;
}
