#nullable enable
using System.Numerics;
using Content.Server.Abilities.Psionics;
using Content.Shared.Abilities.Psionics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.Psionics;

/// <summary>
///     The recurrence field's whole selling point is watching a round hang in the air. If a bullet
///     crosses the bubble at its firing speed the power reads as broken.
/// </summary>
[TestFixture]
[TestOf(typeof(RecurrenceFieldSystem))]
public sealed class RecurrenceFieldCaptureTest
{
    [Test]
    public async Task BulletSlowsInsideTheField()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid bullet = default;
        EntityUid field = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var physics = entMan.System<SharedPhysicsSystem>();

            field = entMan.SpawnEntity("PsionicRecurrenceField", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));

            // Well clear of the field, aimed straight at it at a plausible rifle speed.
            bullet = entMan.SpawnEntity("BulletRifle", new EntityCoordinates(map.Grid, new Vector2(-6.5f, 0.5f)));
            physics.SetLinearVelocity(bullet, new Vector2(60f, 0f));
        });

        var captured = false;
        var minSpeed = float.MaxValue;

        for (var i = 0; i < 60; i++)
        {
            await pair.RunTicksSync(1);

            await server.WaitPost(() =>
            {
                var entMan = server.EntMan;
                if (entMan.Deleted(bullet))
                    return;

                if (entMan.HasComponent<TemporallySlowedComponent>(bullet))
                    captured = true;

                if (entMan.TryGetComponent<PhysicsComponent>(bullet, out var body))
                    minSpeed = MathF.Min(minSpeed, body.LinearVelocity.Length());
            });
        }

        await server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"captured={captured} minSpeed={minSpeed} deleted={server.EntMan.Deleted(bullet)} fieldAlive={!server.EntMan.Deleted(field)}");
            Assert.That(captured, Is.True, "The field never captured the bullet.");
            Assert.That(minSpeed, Is.LessThan(10f), "The bullet was never actually slowed.");
        });

        await pair.CleanReturnAsync();
    }
}
