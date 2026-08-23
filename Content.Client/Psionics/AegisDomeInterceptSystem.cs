using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Projectiles;
using Robust.Client.Player;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client.Psionics;

/// <summary>
/// Stops the shooter's own predicted rounds at an aegis dome, the way the server stops the real ones.
/// </summary>
/// <remarks>
/// With <c>rmc.gun_prediction</c> on, the only round a shooter can see themselves fire is a
/// client-side copy the server has never heard of, and the server's real round is hidden from that
/// player specifically. Interception runs on the server, so without this the shooter watches their
/// fire sail through the barrier and strike the target while the server deleted it at the rim -
/// the dome looks like it does nothing, to the one person whose opinion of it decides whether they
/// keep shooting.
///
/// This deliberately mirrors <c>AegisDomeSystem.Intercept</c> rather than improving on it: a plain
/// "is it inside the radius" test on the same cadence, so both sides stop a round at the same point
/// in its flight. A tighter test here would delete rounds the server lets through, which is worse
/// than the mismatch it fixes - the shooter would see the barrier stop a bullet that still hit.
/// </remarks>
public sealed class AegisDomeInterceptSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// Scratch list so the deletions do not run while the query is still walking the entities.
    /// </summary>
    private readonly List<EntityUid> _stopped = new();

    public override void Initialize()
    {
        base.Initialize();

        // Before the step that would carry the round through the barrier, as on the server.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Prediction replays a tick many times over; the predicted round only actually advances on
        // the first pass, so that is the only one worth testing.
        if (!_timing.IsFirstTimePredicted)
            return;

        _stopped.Clear();

        var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (projectiles.MoveNext(out var uid, out _, out var xform))
        {
            if (IsIntercepted(uid, xform))
                _stopped.Add(uid);
        }

        // The impact flare and the shatter are server-spawned entities that every client already
        // sees, so stopping the round is the whole of this side's job.
        foreach (var uid in _stopped)
            QueueDel(uid);
    }

    private bool IsIntercepted(EntityUid uid, TransformComponent xform)
    {
        var shooter = TryComp<ProjectileComponent>(uid, out var projectile) && projectile.Shooter is { } who
            ? who
            : _player.LocalEntity;

        var position = _transform.GetWorldPosition(xform);

        var domes = EntityQueryEnumerator<AegisDomeComponent, TransformComponent>();
        while (domes.MoveNext(out var domeUid, out var dome, out var domeXform))
        {
            // A shattered dome stops nothing. The entity outlives its integrity by a tick or two.
            if (dome.Integrity <= 0)
                continue;

            var origin = _transform.GetMapCoordinates(domeUid, domeXform);
            if (origin.MapId != xform.MapID)
                continue;

            if (SentFromInside((domeUid, dome), shooter))
                continue;

            var offset = position - origin.Position;
            if (offset.LengthSquared() <= dome.Radius * dome.Radius)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether whoever fired is standing under this dome. The barrier only faces outwards, so people
    /// inside shoot out of it freely.
    /// </summary>
    private bool SentFromInside(Entity<AegisDomeComponent> dome, EntityUid? source)
    {
        if (source is not { } sender)
            return false;

        if (sender == dome.Comp.Caster)
            return true;

        return TryComp<AegisShelteredComponent>(sender, out var sheltered) && sheltered.Dome == dome.Owner;
    }
}
