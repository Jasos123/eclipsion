using System.Numerics;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests
{
    [TestFixture]
    public sealed class ShuttleTest
    {
        [Test]
        public async Task Test()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            await server.WaitIdleAsync();

            var mapMan = server.System<SharedMapSystem>();
            var entManager = server.ResolveDependency<IEntityManager>();
            var physicsSystem = entManager.System<SharedPhysicsSystem>();

            PhysicsComponent gridPhys = null;

            var map = await pair.CreateTestMap();

            await server.WaitAssertion(() =>
            {
                var mapId = map.MapId;
                var grid = map.Grid;

                Assert.Multiple(() =>
                {
                    Assert.That(entManager.HasComponent<ShuttleComponent>(grid));
                    Assert.That(entManager.TryGetComponent(grid, out gridPhys));
                });
                Assert.Multiple(() =>
                {
                    Assert.That(gridPhys.BodyType, Is.EqualTo(BodyType.Dynamic));
                    Assert.That(entManager.GetComponent<TransformComponent>(grid).LocalPosition, Is.EqualTo(Vector2.Zero));
                });
                physicsSystem.ApplyLinearImpulse(grid, Vector2.One, body: gridPhys);
            });

            await server.WaitRunTicks(1);

            await server.WaitAssertion(() =>
            {
                Assert.That(entManager.GetComponent<TransformComponent>(map.Grid).LocalPosition, Is.Not.EqualTo(Vector2.Zero));
            });
            await pair.CleanReturnAsync();
        }

        [Test]
        public void CardinalThrustDoesNotDependOnPerpendicularThrusters()
        {
            var mover = new MoverController();
            var shuttle = new ShuttleComponent();
            var body = new PhysicsComponent();

            shuttle.LinearThrust[(int) Direction.North / 2] = 400f;
            var northThrust = mover.GetDirectionThrust(Vector2.UnitY, shuttle, body);

            shuttle.LinearThrust[(int) Direction.North / 2] = 0f;
            shuttle.LinearThrust[(int) Direction.East / 2] = 250f;
            var eastThrust = mover.GetDirectionThrust(Vector2.UnitX, shuttle, body);

            Assert.Multiple(() =>
            {
                Assert.That(northThrust, Is.EqualTo(Vector2.UnitY * 400f));
                Assert.That(eastThrust, Is.EqualTo(Vector2.UnitX * 250f));
                Assert.That(float.IsFinite(northThrust.X) && float.IsFinite(northThrust.Y), Is.True);
                Assert.That(float.IsFinite(eastThrust.X) && float.IsFinite(eastThrust.Y), Is.True);
            });
        }
    }
}
