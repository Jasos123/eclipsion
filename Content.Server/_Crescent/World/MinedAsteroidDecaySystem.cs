using Content.Server.Gatherable.Components;
using Content.Server.Worldgen.Systems;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._Crescent.World;
using Content.Shared.Ghost;
using Content.Shared.Whitelist;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.World;

/// <summary>
/// Cleans up asteroids that players have hollowed out and abandoned.
/// </summary>
/// <remarks>
/// Depletion is measured in rock, not tiles: a pickaxe leaves the floor behind and only the ship drill ever clears
/// tiles, so counting tiles would miss hand mining entirely.
/// </remarks>
public sealed class MinedAsteroidDecaySystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// How often the whole set of asteroids is walked. Individual rocks are surveyed far less often than this.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextSweep;

    private EntityQuery<GatherableComponent> _gatherQuery;
    private EntityQuery<GhostComponent> _ghostQuery;

    public override void Initialize()
    {
        base.Initialize();

        // Worldgen only fills the rock in once a player loads the chunk, so the baseline has to wait for the
        // populator to finish rather than for map init.
        SubscribeLocalEvent<MinedAsteroidDecayComponent, LocalStructureLoadedEvent>(OnStructureLoaded,
            after: new[] { typeof(SimpleFloorPlanPopulatorSystem) });

        _gatherQuery = GetEntityQuery<GatherableComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();
    }

    private void OnStructureLoaded(EntityUid uid, MinedAsteroidDecayComponent component,
        LocalStructureLoadedEvent args)
    {
        // The loader removes its component deferred, so this can fire twice. Re-baselining a half-mined rock would
        // reset its depletion to zero.
        if (component.Baselined)
            return;

        component.Baselined = true;
        component.InitialRock = Survey(uid, null).Rock;

        // Stagger the first survey so a freshly loaded belt does not recount itself all on one tick.
        component.NextCheck = _timing.CurTime + _random.Next(component.CheckInterval);
    }

    public override void Update(float frameTime)
    {
        _nextSweep -= TimeSpan.FromSeconds(frameTime);
        if (_nextSweep > TimeSpan.Zero)
            return;

        _nextSweep = SweepInterval;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MinedAsteroidDecayComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            // No baseline means worldgen never populated this rock, so nobody has ever been near it.
            if (comp.InitialRock <= 0 || now < comp.NextCheck)
                continue;

            comp.NextCheck = now + comp.CheckInterval;

            // Mapping mode. Leave the map exactly as the mapper left it.
            if (!_map.IsInitialized(xform.MapID))
                continue;

            var (rock, claimed) = Survey(uid, comp.ClaimWhitelist);

            // A generator or seep drill bolted to the rock means a crew is working it, so the clock never starts.
            if (claimed)
            {
                comp.DecayAt = null;
                continue;
            }

            var mined = 1f - (float) rock / comp.InitialRock;
            if (mined < comp.DepletionThreshold)
            {
                comp.DecayAt = null;
                continue;
            }

            if (comp.DecayAt is not { } decayAt)
            {
                comp.DecayAt = now + comp.DecayDelay;
                continue;
            }

            if (now < decayAt)
                continue;

            // Don't pop a grid in someone's face. DecayAt stays in the past, so the next sweep retries.
            if (PlayerNearby(xform, comp.PlayerSafeRange))
                continue;

            Log.Info(
                $"Deleting mined-out asteroid {ToPrettyString(uid)}: {rock} of {comp.InitialRock} rock left, unclaimed.");
            QueueDel(uid);
        }
    }

    /// <summary>
    /// Counts the rock still standing on the grid and reports whether anything on it claims the asteroid, in one
    /// pass over the grid's children.
    /// </summary>
    private (int Rock, bool Claimed) Survey(EntityUid uid, EntityWhitelist? claim)
    {
        var rock = 0;
        var claimed = false;

        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_gatherQuery.HasComp(child))
            {
                rock++;
                continue;
            }

            if (!claimed && claim != null && _whitelist.IsValid(claim, child))
                claimed = true;
        }

        return (rock, claimed);
    }

    private bool PlayerNearby(TransformComponent xform, float range)
    {
        if (xform.MapUid is not { } mapUid)
            return false;

        var origin = _transform.GetWorldPosition(xform);
        var rangeSquared = range * range;

        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var actor, out _, out var actorXform))
        {
            // Ghosts and admins in observe mode shouldn't be able to pin an asteroid in place forever.
            if (_ghostQuery.HasComp(actor))
                continue;

            if (actorXform.MapUid != mapUid)
                continue;

            if ((_transform.GetWorldPosition(actorXform) - origin).LengthSquared() <= rangeSquared)
                return true;
        }

        return false;
    }
}
