#nullable enable
using System.Numerics;
using Content.Server.Abilities.Psionics;
using Content.Shared.Abilities.Psionics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests.Psionics;

/// <summary>
///     The recurrence field's whole selling point is watching a round hang in the air. If a bullet
///     crosses the bubble at its firing speed the power reads as broken.
/// </summary>
[TestFixture]
[TestOf(typeof(RecurrenceFieldSystem))]
public sealed class RecurrenceFieldCaptureTest
{
    /// <summary>
    ///     Rounds are caught on the rim whatever they are doing on the way in. The swept test is the
    ///     load-bearing part: the quickest of these clears the whole bubble inside a single tick, so
    ///     a check that only asked "is it inside right now" would never see it.
    /// </summary>
    [Test]
    public async Task RoundsAreCaughtAtEverySpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        foreach (var speed in new[] { 25f, 50f, 100f, 200f, 400f })
        {
            EntityUid bullet = default;
            EntityUid field = default;

            await server.WaitPost(() =>
            {
                var entMan = server.EntMan;

                field = entMan.SpawnEntity("PsionicRecurrenceField", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));

                // Far enough back to still be outside the bubble on the tick it is spawned, whatever
                // the speed: a round only gets one swept test per tick to be caught by.
                bullet = entMan.SpawnEntity("BulletRifle", new EntityCoordinates(map.Grid, new Vector2(-30.5f, 0.5f)));
                entMan.System<SharedPhysicsSystem>().SetLinearVelocity(bullet, new Vector2(speed, 0f));
            });

            var captured = false;
            var slowest = float.MaxValue;

            for (var i = 0; i < 40; i++)
            {
                await pair.RunTicksSync(1);

                await server.WaitPost(() =>
                {
                    var entMan = server.EntMan;
                    if (entMan.Deleted(bullet) || !entMan.HasComponent<TemporallySlowedComponent>(bullet))
                        return;

                    captured = true;

                    if (entMan.TryGetComponent<PhysicsComponent>(bullet, out var body))
                        slowest = MathF.Min(slowest, body.LinearVelocity.Length());
                });
            }

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.True, $"A {speed}m/s round crossed the field untouched.");

                // The field holds a round to a fraction of what it arrived with, so the ceiling has
                // to be read off the entry speed rather than a flat number.
                Assert.That(slowest, Is.LessThan(speed * 0.1f),
                    $"A {speed}m/s round was still doing {slowest}m/s inside the field.");
            });

            await server.WaitPost(() =>
            {
                var entMan = server.EntMan;
                if (!entMan.Deleted(bullet))
                    entMan.DeleteEntity(bullet);
                if (!entMan.Deleted(field))
                    entMan.DeleteEntity(field);
            });

            await pair.RunTicksSync(2);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    ///     A round crawling across the bubble spends most of its life doing it. Its despawn timer has
    ///     to be held back with it, or the field quietly eats bullets instead of holding them.
    /// </summary>
    [Test]
    public async Task DespawnTimerIsHeldWithTheRound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid bullet = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;

            entMan.SpawnEntity("PsionicRecurrenceField", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            bullet = entMan.SpawnEntity("BulletRifle", new EntityCoordinates(map.Grid, new Vector2(-4.5f, 0.5f)));
            entMan.System<SharedPhysicsSystem>().SetLinearVelocity(bullet, new Vector2(25f, 0f));
        });

        // Long enough that an unheld timer would visibly bleed away.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;

            Assert.That(entMan.Deleted(bullet), Is.False, "The field deleted the round it was holding.");
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(bullet), Is.True,
                "The round left the field far too early to have been slowed.");

            var despawn = entMan.GetComponent<TimedDespawnComponent>(bullet);
            var lifetime = entMan.GetComponent<TimedDespawnComponent>(
                entMan.SpawnEntity("BulletRifle", new EntityCoordinates(map.Grid, new Vector2(20.5f, 0.5f)))).Lifetime;

            // Two seconds of holding at 3% gives back all but a sliver of the two seconds. Comparing
            // against a fresh round rather than a literal keeps this honest if the prototype changes.
            Assert.That(despawn.Lifetime, Is.GreaterThan(lifetime - 0.5f),
                "The held round's despawn timer ran at full speed inside the field.");
        });

        await pair.CleanReturnAsync();
    }
}
