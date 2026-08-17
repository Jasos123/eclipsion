using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._Crescent.ShipAI;

/// <summary>
///     Passes while our ship's remaining hull sits inside the given band. Use it to give a ship NPC a branch
///     it only takes once it is losing - the planner re-runs every plan cycle, so a lower-indexed branch
///     guarded by this will preempt an attack plan already in flight.
/// </summary>
public sealed partial class ShipHullIntegrityPrecondition : HTNPrecondition
{
    private ShipHullMonitorSystem _hull = default!;

    /// <summary>
    ///     Highest remaining hull fraction this still accepts. Leave at 1 for "no upper bound".
    /// </summary>
    [DataField]
    public float MaxIntegrity = 1f;

    /// <summary>
    ///     Lowest remaining hull fraction this still accepts.
    /// </summary>
    [DataField]
    public float MinIntegrity = 0f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);

        _hull = sysManager.GetEntitySystem<ShipHullMonitorSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // An unmonitored ship reads as undamaged, so adding this precondition to a prototype that never got a
        // ShipHullMonitor changes nothing instead of silently disabling the branch it guards.
        var integrity = _hull.GetIntegrity(owner);

        return integrity <= MaxIntegrity && integrity >= MinIntegrity;
    }
}
