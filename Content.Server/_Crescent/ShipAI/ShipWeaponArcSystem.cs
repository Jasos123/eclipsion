using Content.Server.PointCannons;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared.GameTicking;
using Content.Shared.PointCannons;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.ShipAI;

/// <summary>
///     Works out which way a ship should be turned to bring the most guns to bear, so an AI stops pointing its
///     nose at a target it cannot shoot from the nose.
/// </summary>
/// <remarks>
///     <para>
///     A <see cref="PointCannonComponent"/> stores the arcs it *cannot* fire through as grid-local angles, and
///     <see cref="PointCannonSystem.SafetyCheck"/> tests a bearing against them. That bearing is the plain
///     atan2 angle to the target expressed in the grid's frame - see how <see cref="PointCannonSystem.TryFireCannon"/>
///     builds it. A ship with its nose on the target sits at exactly pi/2 in that frame, because shuttles face
///     grid-north.
///     </para>
///     <para>
///     So the answer this hands back is a rotation offset in degrees, ready for
///     <see cref="Content.Server._Mono.NPC.HTN.ShipSteererComponent.TargetRotation"/>: offset = 90 - bestBearing.
///     </para>
/// </remarks>
public sealed class ShipWeaponArcSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PointCannonSystem _cannon = default!;

    /// <summary>
    ///     Bearings tested around the full circle. 72 gives 5 degree resolution and, being divisible by four,
    ///     lands exactly on the nose bearing - which matters, because that is the tie-break winner.
    /// </summary>
    private const int Samples = 72;

    /// <summary>
    ///     Grid-local bearing to a target that a ship pointing its nose straight at it produces.
    /// </summary>
    private static readonly Angle NoseBearing = Math.PI / 2;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How far off its own axis a fixed-mount gun still counts as on target. A fixed gun never swings, so this
    ///     is a real aiming error rather than a search tolerance - a couple of degrees is about all that still
    ///     lands at the ranges these hulls fight at. It only costs anything when a hull's fixed guns are not
    ///     parallel and the solver has to split the difference between them: each axis is offered as a bearing in
    ///     its own right below, so a bank of parallel guns is aimed exactly rather than to the nearest sample.
    /// </summary>
    private static readonly double FixedMountTolerance = PointCannonSystem.FixedMountAimTolerance.Theta;

    /// <summary>
    ///     Slack for comparing two candidates' aiming error, which is a sum of doubles and so never lands on an
    ///     exact tie of its own accord.
    /// </summary>
    private const double ErrorEpsilon = 1e-6;

    private EntityQuery<GunComponent> _gunQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<HardpointFixedMountComponent> _fixedMountQuery;

    /// <summary>
    ///     Per-grid answers. Entity uids are recycled between rounds, so this has to be dropped on restart.
    ///     Entries are also dropped as their grid dies: a carrier rebuilds its squadron all round, and every
    ///     drone that blows up is a grid we would otherwise keep an answer for until the round ended.
    /// </summary>
    private readonly Dictionary<EntityUid, (TimeSpan Expiry, float Offset)> _cache = new();

    private readonly HashSet<Entity<PointCannonComponent>> _cannonScratch = new();

    /// <summary>
    ///     The cannons from <see cref="_cannonScratch"/> that can actually shoot, each with the fixed axis it is
    ///     welded to, or null when it is a turret free to swing onto the target.
    /// </summary>
    private readonly List<(Entity<PointCannonComponent> Cannon, Angle? Axis)> _usable = new();

    /// <summary>
    ///     Bearings worth scoring: the uniform sample grid, plus the exact axis of every fixed gun aboard. The
    ///     grid on its own never lands on an axis, so without those a welded battery is only ever aimed to the
    ///     nearest sample and its shots leave permanently wide.
    /// </summary>
    private readonly List<Angle> _candidates = new();

    /// <summary>
    ///     Cannons we have had obstruction arcs worked out for, mapped to the hardpoint they were measured on.
    ///     See <see cref="EnsureRanges"/>.
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid> _measured = new();

    public override void Initialize()
    {
        base.Initialize();

        _gunQuery = GetEntityQuery<GunComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _fixedMountQuery = GetEntityQuery<HardpointFixedMountComponent>();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _cache.Clear();
            _measured.Clear();
        });

        // Broadcast, not a MapGridComponent/ComponentShutdown pair - the engine's SharedMapSystem already owns
        // that pair, and a second subscription to it is a hard startup crash rather than two handlers.
        SubscribeLocalEvent<GridRemovalEvent>(ev => _cache.Remove(ev.EntityUid));

        // Losing a battery changes the answer, so drop the ship's cached bearing instead of letting it keep
        // turning toward guns that no longer exist for the rest of the cache lifetime.
        SubscribeLocalEvent<PointCannonComponent, ComponentShutdown>(OnCannonShutdown);
    }

    private void OnCannonShutdown(Entity<PointCannonComponent> ent, ref ComponentShutdown args)
    {
        _measured.Remove(ent.Owner);

        if (Transform(ent.Owner).GridUid is { } gridUid)
            _cache.Remove(gridUid);
    }

    /// <summary>
    ///     Rotation offset in degrees that puts the most of <paramref name="gridUid"/>'s cannons on target,
    ///     for feeding straight into a steerer's TargetRotation. 0 means "nose at the target", which is both
    ///     the answer for a normal forward-armed ship and the fallback when we cannot work anything out.
    /// </summary>
    public float GetFiringOffset(EntityUid gridUid)
    {
        var now = _timing.CurTime;

        if (_cache.TryGetValue(gridUid, out var cached) && now < cached.Expiry)
            return cached.Offset;

        var offset = Compute(gridUid);
        _cache[gridUid] = (now + CacheLifetime, offset);

        return offset;
    }

    private float Compute(EntityUid gridUid)
    {
        if (!_gridQuery.TryComp(gridUid, out var grid))
            return 0f;

        _cannonScratch.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, grid.LocalAABB, _cannonScratch);

        if (_cannonScratch.Count == 0)
            return 0f;

        // Sift once, up front: the per-cannon conditions below don't vary with bearing, so testing them inside
        // the sampling loop just repeated the same component lookups 72 times over.
        _usable.Clear();
        _candidates.Clear();
        foreach (var cannon in _cannonScratch)
        {
            // Exactly the standing conditions the firing path applies - see PointCannonSystem.CanCannonFire, which
            // TryFireCannon itself goes through. Checking only "anchored, and has a gun" counted guns bolted to
            // nothing and guns on a dead hardpoint, so a hull would turn its unusable side to the enemy.
            var xform = Transform(cannon.Owner);
            if (!xform.Anchored
                || !_gunQuery.HasComp(cannon.Owner)
                || !_cannon.CanCannonFire(cannon.Owner, out var hardpoint)
                || !HasAmmo(cannon.Owner))
            {
                continue;
            }

            EnsureRanges(cannon, hardpoint);

            // A fixed mount never swings to face anything: TryFireCannon only rotates a cannon whose hardpoint
            // is NOT a HardpointFixedMount, and otherwise fires straight down the cannon's own axis. So instead
            // of asking "is the target bearing clear for this gun", a fixed gun only counts on the bearings that
            // line up with the axis it is welded to.
            if (!_fixedMountQuery.HasComp(hardpoint))
            {
                _usable.Add((cannon, null));
                continue;
            }

            var axis = new Angle(xform.LocalRotation - Math.PI / 2);

            // Its firing direction never changes, so its arc check is settled once here instead of per bearing:
            // a fixed gun pointing into its own hull is dead weight the ship must not turn on account of.
            if (!_cannon.SafetyCheck(axis, cannon.Comp))
                continue;

            _usable.Add((cannon, axis));
            _candidates.Add(axis);
        }

        if (_usable.Count == 0)
            return 0f;

        for (var i = 0; i < Samples; i++)
        {
            _candidates.Add(new Angle(Math.Tau * i / Samples));
        }

        var bestCount = -1;
        var bestError = 0d;
        var bestOffset = 0f;

        foreach (var bearing in _candidates)
        {
            var offset = (float) Angle.ShortestDistance(bearing, NoseBearing).Degrees;

            var count = 0;
            var error = 0d;
            foreach (var (cannon, axis) in _usable)
            {
                if (axis is { } fixedAxis)
                {
                    // Already known to be clear; all that is left is whether turning the hull to this bearing
                    // puts the target close enough to the axis for the shot to land.
                    var delta = Math.Abs(Angle.ShortestDistance(fixedAxis, bearing).Theta);
                    if (delta > FixedMountTolerance)
                        continue;

                    count++;
                    error += delta;
                    continue;
                }

                // A turret swings onto the target, so the shot leaves along the bearing itself.
                if (_cannon.SafetyCheck(bearing, cannon.Comp))
                    count++;
            }

            // Most guns wins. Then the bearing that lines the fixed ones up best, because a gun counted a few
            // degrees off its own axis is a gun that misses - several bearings can claim the same fixed battery,
            // and only one of them actually aims it. Least turn breaks what is left, and the nose is a sampled
            // bearing with an offset of exactly 0, so an all-turret ship still resolves nose-on exactly as it did
            // before this system existed.
            var better = count > bestCount
                || count == bestCount && error < bestError - ErrorEpsilon
                || count == bestCount && error <= bestError + ErrorEpsilon && MathF.Abs(offset) < MathF.Abs(bestOffset);

            if (!better)
                continue;

            bestCount = count;
            bestError = error;
            bestOffset = offset;
        }

        return bestOffset;
    }

    /// <summary>
    ///     Whether a gun has anything left to fire with. Unlike the mounting and power conditions this one is not
    ///     in the firing path at all: <c>AttemptShoot</c> runs an empty gun, plays the click and returns, and
    ///     <c>TryFireCannon</c> reports success either way. So an empty battery would weigh exactly as much in the
    ///     count as a loaded one and a hull could sit presenting its dry side to the enemy.
    /// </summary>
    /// <remarks>
    ///     Read at most once per cache lifetime per grid, so a battery that runs dry mid-engagement keeps its vote
    ///     until the answer is recomputed. Dirtying the cache on every round fired would rebuild the arcs of every
    ///     gun aboard several times a second, which is a far worse trade than a few seconds of stale bearing.
    /// </remarks>
    private bool HasAmmo(EntityUid cannon)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(cannon, ref ev);

        // Capacity 0 means nothing answered - a gun with no ammo provider we know how to read, rather than an
        // empty one. Assume it shoots instead of telling the ship to turn away from it.
        return ev.Capacity == 0 || ev.Count > 0;
    }

    /// <summary>
    ///     Makes sure a cannon's obstruction arcs have actually been worked out before we read them.
    /// </summary>
    /// <remarks>
    ///     <see cref="PointCannonComponent.ObstructedRanges"/> starts empty and is normally only filled in by
    ///     <c>PointCannonSystem.LinkCannon</c> - that is, when a targeting console picks the cannon up - or by
    ///     being baked into the map. An AI hull has neither: none of the drone shuttles carry a console or
    ///     serialised arcs. Without this every arc reads as clear, all 72 bearings tie, and the tie-break hands
    ///     back nose-on for a ship whose guns were never measured at all.
    /// </remarks>
    private void EnsureRanges(Entity<PointCannonComponent> cannon, EntityUid hardpoint)
    {
        // Keyed on the hardpoint, not just the cannon: arcs describe where this gun sits on this hull, so a
        // gun unbolted and remounted somewhere else needs measuring again. PointCannonSystem only drops a grid
        // cache when a cannon is re-anchored - it never recomputes the ranges - so nothing else would.
        var known = _measured.TryGetValue(cannon.Owner, out var measuredAt);

        if (known && measuredAt == hardpoint)
            return;

        _measured[cannon.Owner] = hardpoint;

        // Whatever it holds now describes the mounting it just left, so it has to be redone even though it is
        // non-empty. Only on a gun we have never seen do we trust an existing list: that one came from the map
        // or from a targeting console, and re-deriving it would throw away a mapper's baked arcs.
        // Same clearance radius a targeting console would have used, so a gun we measure ourselves and one
        // the console measured describe the same hull.
        if (known || cannon.Comp.ObstructedRanges.Count == 0)
            _cannon.RefreshFiringRanges(cannon.Owner, null, null, cannon.Comp, PointCannonSystem.CannonCheckRange);
    }
}
