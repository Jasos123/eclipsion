using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Shared._Crescent.CCVar
{
    [CVarDefs]
    public sealed class RatCCVars : CVars
    {
        /// <summary>
        /// Whether automatic debris cleanup is enabled.
        /// </summary>
        public static readonly CVarDef<bool> TrashCleanupEnabled =
            CVarDef.Create("trash.cleanup_enabled", false, CVar.SERVERONLY);

        /// <summary>
        /// Interval between debris cleanups, in seconds.
        /// </summary>
        public static readonly CVarDef<float> TrashCleanupInterval =
            CVarDef.Create("trash.cleanup_interval", 60f, CVar.SERVERONLY);

        /// <summary>
        /// Delay after round start before debris cleanup switches on, in seconds.
        /// </summary>
        public static readonly CVarDef<float> TrashCleanupStartDelay =
            CVarDef.Create("trash.cleanup_start_delay", 60f, CVar.SERVERONLY);

        /// <summary>
        /// Enables/disables automatic deletion of small grids.
        /// </summary>
        public static readonly CVarDef<bool> AutoGridCleanupEnabled =
            CVarDef.Create("shuttle.grid_cleanup_enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

        /// <summary>
        /// Whether population-scaled join caps are enforced. Off means the counts are still tracked and
        /// shown in the late-join window, but nobody is refused.
        /// </summary>
        public static readonly CVarDef<bool> FactionBalanceEnabled =
            CVarDef.Create("game.faction_balance_enabled", true, CVar.SERVERONLY | CVar.ARCHIVE);

        /// <summary>
        /// Headcount every balanced faction may always reach regardless of the population maths, so an
        /// empty or nearly empty server stays joinable.
        /// </summary>
        public static readonly CVarDef<int> FactionBalanceBaseSlots =
            CVarDef.Create("game.faction_balance_base_slots", 3, CVar.SERVERONLY | CVar.ARCHIVE);

        /// <summary>
        /// Extra headroom on top of each faction's computed share. 0 holds the parity factions to a lead
        /// of one player; raise it to loosen the grip.
        /// </summary>
        public static readonly CVarDef<int> FactionBalanceTolerance =
            CVarDef.Create("game.faction_balance_tolerance", 0, CVar.SERVERONLY | CVar.ARCHIVE);

        /// <summary>
        /// Whether admins ignore the join caps.
        /// </summary>
        public static readonly CVarDef<bool> FactionBalanceAdminBypass =
            CVarDef.Create("game.faction_balance_admin_bypass", true, CVar.SERVERONLY | CVar.ARCHIVE);
    }
}