#nullable enable
using System.Numerics;
using Content.Shared._Crescent.World;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class AsteroidDriftTest
{
    private const float BeltRadius = 4250f;
    private const float ExpectedSpeed = 20f;

    [Test]
    public async Task OuterBeltAsteroidCoastsAroundTheOrigin()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();

        EntityUid roid = default;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            roid = entMan.SpawnEntity("RatAsteroidPoorLarge", new MapCoordinates(new Vector2(BeltRadius, 0f), mapId));

            // Use a short period so movement is measurable in a few ticks.
            entMan.GetComponent<OrbitingDebrisComponent>(roid).Period =
                TimeSpan.FromSeconds(MathF.Tau * BeltRadius / ExpectedSpeed);
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var pos = xformSys.GetWorldPosition(roid);
            var body = entMan.GetComponent<PhysicsComponent>(roid);

            Assert.Multiple(() =>
            {
                Assert.That(body.BodyType, Is.EqualTo(BodyType.Dynamic));

                Assert.That(body.LinearDamping, Is.EqualTo(0f).Within(0.0001f),
                    "Drifting debris has to have its damping cleared or it coasts to a halt.");
                Assert.That(body.LinearVelocity.Length(), Is.EqualTo(ExpectedSpeed).Within(0.5f),
                    "The rock should be held at the tangential speed its period implies.");

                Assert.That(pos.Y, Is.GreaterThan(5f), "The asteroid should have coasted along its orbit.");
                Assert.That(pos.Length(), Is.EqualTo(BeltRadius).Within(2f),
                    "Velocity is re-aimed along the tangent, so the orbit radius has to hold.");

                Assert.That(body.AngularVelocity, Is.EqualTo(0f).Within(0.0001f),
                    "A spinning asteroid would drag an anchored drill around with it.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreBeltAsteroidStaysPut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();

        EntityUid roid = default;
        var start = Vector2.Zero;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);

            // This debris is inside minRadius and must remain stationary.
            roid = entMan.SpawnEntity("RatAsteroidRichLarge", new MapCoordinates(new Vector2(400f, 0f), mapId));
            entMan.GetComponent<OrbitingDebrisComponent>(roid).Period = TimeSpan.FromSeconds(10);
            start = xformSys.GetWorldPosition(roid);
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(roid);

            Assert.Multiple(() =>
            {
                Assert.That((xformSys.GetWorldPosition(roid) - start).Length(), Is.LessThan(0.01f),
                    "Asteroids inside the exclusion radius must not drift.");
                Assert.That(body.LinearVelocity.Length(), Is.EqualTo(0f).Within(0.0001f));

                // The default damping confirms drift setup did not run.
                Assert.That(body.LinearDamping, Is.GreaterThan(0f),
                    "A rock inside the exclusion radius must be left exactly as worldgen made it.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
