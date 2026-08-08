using System.Numerics;
using Content.Shared._Crescent.World;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Crescent.World;

/// <summary>
/// Updates the velocity of orbiting worldgen debris.
/// </summary>
public sealed class OrbitingDebrisSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        _nextUpdate -= TimeSpan.FromSeconds(frameTime);
        if (_nextUpdate > TimeSpan.Zero)
            return;

        _nextUpdate = UpdateInterval;

        var query = EntityQueryEnumerator<OrbitingDebrisComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var orbit, out var body, out var xform))
        {
            if (xform.MapUid == null || orbit.Period <= TimeSpan.Zero)
                continue;

            var offset = _transform.GetWorldPosition(xform) - orbit.Center;
            var radius = offset.Length();

            if (radius < orbit.MinRadius || radius <= 0f)
            {
                if (orbit.Started)
                    _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
                continue;
            }

            if (!orbit.Started)
            {
                StartDrifting(uid, body);
                orbit.Started = true;
            }

            // The unnormalized tangent gives v = wr after scaling by angular rate.
            var tangent = orbit.Clockwise
                ? new Vector2(offset.Y, -offset.X)
                : new Vector2(-offset.Y, offset.X);

            var angularRate = MathF.Tau / (float) orbit.Period.TotalSeconds;
            _physics.SetLinearVelocity(uid, tangent * angularRate, body: body);
        }
    }

    private void StartDrifting(EntityUid uid, PhysicsComponent body)
    {
        _physics.SetBodyType(uid, BodyType.Dynamic, body: body);
        _physics.SetBodyStatus(uid, body, BodyStatus.InAir);
        _physics.SetFixedRotation(uid, true, body: body);
        _physics.SetLinearDamping(uid, body, 0f);
        _physics.SetAngularDamping(uid, body, 0f);
        _physics.SetAngularVelocity(uid, 0f, body: body);
    }
}
