#nullable enable
using System.Numerics;
using Content.Server.Abilities.Psionics;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions.Events;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Psionics;

/// <summary>
/// Covers the two halves of the recurrence field that only exist at runtime: capturing a moving
/// object without a physics volume, and turning it around when the field is collapsed.
/// </summary>
[TestFixture]
[TestOf(typeof(RecurrenceFieldSystem))]
public sealed class RecurrenceFieldTest
{
    private const string FieldProto = "PsionicRecurrenceField";

    // A thrown item is the simplest thing that is dynamic, unarmed and safe to spawn in a bare map.
    private const string ProjectileStandIn = "Crowbar";

    // An actual round, which is a different case to the stand-in above and not a cosmetic one: every
    // projectile in the game is a non-hard fixture, and a lookup that does not ask for those gets
    // back the thrown crowbar and none of the bullets.
    private const string BulletProto = "BulletPistol";

    // What the field measures across, and what the sprite and the light are sized to. The forward
    // sweep only has anything to prove while a tick of travel is wider than this.
    private const float FieldDiameter = 4f;

    // A mob, which is the only thing the movement half of the field applies to.
    private const string MobProto = "MobHuman";

    // Capture runs every tick; the margin is for the tick the spawn itself lands on. Kept short
    // because a captured object still drifts, and the field is only a couple of tiles across.
    private const int ScanTicks = 6;

    [Test]
    public async Task FieldSlowsWhatEntersItAndRestoresItOnRelease()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();

        EntityUid field = default;
        EntityUid thrown = default;
        var launchSpeed = 6f;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            field = entMan.SpawnEntity(FieldProto, new MapCoordinates(Vector2.Zero, mapId));
            thrown = entMan.SpawnEntity(ProjectileStandIn, new MapCoordinates(Vector2.Zero, mapId));
            physics.SetLinearVelocity(thrown, new Vector2(launchSpeed, 0f));
        });

        await pair.RunTicksSync(ScanTicks);

        var slowedSpeed = 0f;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(field), Is.False,
                "A field spawned straight from the prototype has to inherit its own lifetime.");
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(thrown), Is.True,
                "An object moving through the field should have been captured by the scan.");

            var slowed = entMan.GetComponent<TemporallySlowedComponent>(thrown);
            slowedSpeed = entMan.GetComponent<PhysicsComponent>(thrown).LinearVelocity.Length();

            Assert.Multiple(() =>
            {
                Assert.That(slowed.AppliedScale, Is.LessThan(1f),
                    "A captured object has to record the scale that was applied to it.");
                Assert.That(slowed.EntryVelocity.Length(), Is.GreaterThan(1f),
                    "The pulse aims down the entry velocity, so capture has to remember it.");
                Assert.That(slowedSpeed, Is.LessThan(launchSpeed * 0.5f),
                    "Capture is supposed to be a hard slow, not a nudge.");
            });

            // Deleting the field is the same path an expiry takes.
            entMan.DeleteEntity(field);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(thrown), Is.False,
                "A collapsed field must not leave anything frozen behind it.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(thrown).LinearVelocity.Length(),
                Is.GreaterThan(slowedSpeed * 2f),
                "Release has to undo exactly what capture did, not leave the object crawling.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PulseThrowsHeldObjectsBackTheWayTheyCame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();

        EntityUid caster = default;
        EntityUid field = default;
        EntityUid thrown = default;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            caster = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(-3f, 0f), mapId));
            entMan.AddComponent<PsionicComponent>(caster);

            field = entMan.SpawnEntity(FieldProto, new MapCoordinates(Vector2.Zero, mapId));
            entMan.GetComponent<RecurrenceFieldComponent>(field).Caster = caster;

            // Travelling in +X, so the pulse has to send it back along -X.
            thrown = entMan.SpawnEntity(ProjectileStandIn, new MapCoordinates(Vector2.Zero, mapId));
            physics.SetLinearVelocity(thrown, new Vector2(8f, 0f));
        });

        await pair.RunTicksSync(ScanTicks);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(thrown), Is.True,
                "Nothing to pulse if the field never caught it.");

            var ev = new PsionicRecurrencePulseActionEvent { Performer = caster };
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(field), Is.True, "The pulse consumes the field.");

            var velocity = entMan.GetComponent<PhysicsComponent>(thrown).LinearVelocity;
            Assert.That(velocity.X, Is.LessThan(0f),
                "The object came in heading +X, so it has to leave heading -X.");
            Assert.That(velocity.Length(), Is.GreaterThan(10f),
                "A returned object should be moving at least as fast as it arrived.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The case the field exists for. Most rounds in this game travel at 60-200m/s and cross a 4m
    /// bubble inside a single tick, so capture cannot be a question of where something happens to be
    /// standing when a scan runs.
    /// </summary>
    [Test]
    public async Task FastProjectileIsCaughtInsteadOfPassingStraightThrough()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();

        EntityUid field = default;
        EntityUid fast = default;

        // Quicker than anything in the prototypes, so one tick of travel is wider than the field.
        const float speed = 300f;

        // Placed exactly half a tick of travel short of the centre, which puts it clear of the
        // field on this tick and clear of it again on the next one without ever being inside on a
        // tick boundary. Derived from the real tick period rather than assumed, because the whole
        // point of the case is that it depends on how far a round moves between two scans.
        var travel = speed * (float) server.ResolveDependency<IGameTiming>().TickPeriod.TotalSeconds;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            field = entMan.SpawnEntity(FieldProto, new MapCoordinates(Vector2.Zero, mapId));

            fast = entMan.SpawnEntity(BulletProto, new MapCoordinates(new Vector2(-travel / 2f, 0f), mapId));
            physics.SetLinearVelocity(fast, new Vector2(speed, 0f));
        });

        // Short on purpose: even at a twelfth of its speed this round is still crossing the field,
        // and the point is what happens on the tick it arrives.
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(fast), Is.False, "The round should not have been deleted.");
            Assert.That(travel, Is.GreaterThan(FieldDiameter),
                "The case only exists if a tick of travel is wider than the field itself.");
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(fast), Is.True,
                "A round quick enough to clear the field between two ticks still has to be caught.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(fast).LinearVelocity.Length(),
                Is.LessThan(speed * 0.5f),
                "Catching it is only worth anything if it is actually slowed down.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Mobs are slowed through the movement modifier rather than through physics, which is the one
    /// path where a mistake sticks to the player after the field is long gone.
    /// </summary>
    [Test]
    public async Task MobIsSlowedInsideTheFieldAndWalksNormallyAfterIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();

        EntityUid field = default;
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            mapSys.CreateMap(out var mapId);
            field = entMan.SpawnEntity(FieldProto, new MapCoordinates(Vector2.Zero, mapId));
            mob = entMan.SpawnEntity(MobProto, new MapCoordinates(Vector2.Zero, mapId));
        });

        await pair.RunTicksSync(ScanTicks);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(mob), Is.True,
                "A mob standing in the field has to be captured by it.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier,
                Is.LessThan(1f),
                "The server has to apply the slow it tells the client about, not just record it.");

            entMan.DeleteEntity(field);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<TemporallySlowedComponent>(mob), Is.False,
                "A collapsed field must not leave anyone marked as held by it.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier,
                Is.EqualTo(1f).Within(0.001f),
                "Leaving the field has to give the walk speed back - the slow must not outlive it.");
        });

        await pair.CleanReturnAsync();
    }
}
