using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Crescent.CCvars;

[CVarDefs]
public sealed class CrescentCVars : CVars
{
    /// <summary>
    /// Whether or not respawning is enabled.
    /// </summary>
    public static readonly CVarDef<bool> RespawnEnabled =
        CVarDef.Create("sc.respawn.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Respawn time, how long the player has to wait in seconds after death. 
    /// 
    /// HULLROT NOTE: this does NOT work. use the timer in Content.Shared/CCVar/CCVars.GhostRespawn.cs
    /// </summary>
    public static readonly CVarDef<float> RespawnTime =
        CVarDef.Create("sc.respawn.time", 69.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Enforce role whitelists
    /// </summary>
    public static readonly CVarDef<bool> RoleWhitelist =
        CVarDef.Create("sc.role_whitelist", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// UI Layouts. Here to not conflict with other codebases. (They all share the same config file lol :csgrad:)
    /// </summary>
    public static readonly CVarDef<string> UILayout =
        CVarDef.Create("sc.ui.layout", "Classic", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// How often point cannons will be updated, in ticks per second.
    /// </summary>
    public static readonly CVarDef<float> PointCannonUiTps =
        CVarDef.Create("sc.pointcannons.ui_tps", 10.0f, CVar.SERVERONLY);

    /// <summary>
    /// HULLROT: How often shuttle consoles will be updated, in ticks per second.
    /// </summary>
    public static readonly CVarDef<float> ShuttleConsoleUiTps =
        CVarDef.Create("shuttle.console_tps", 10.0f, CVar.SERVERONLY);

    /// <summary>
    /// Radar console updates per second. 0 (the default) means every tick, which is what radar has always done.
    /// Projectile blips are drawn straight from these snapshots with no smoothing in between, so lowering this
    /// makes them visibly step. Only worth turning down on a host that is measurably struggling.
    /// </summary>
    public static readonly CVarDef<float> RadarConsoleUiTps =
        CVarDef.Create("radar.console_tps", 0f, CVar.SERVERONLY);

    /// <summary>
    /// Whether ship weapons and shield emitters consume power.
    /// Keep disabled until existing ship maps have been updated for the new power budget.
    /// A server restart is required after changing this value.
    /// </summary>
    public static readonly CVarDef<bool> ShipSystemsPowerDrawEnabled =
        CVarDef.Create("sc.ship_systems_power_draw_enabled", false, CVar.SERVERONLY);
}
