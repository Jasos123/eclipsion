using Content.Server.Doors.Systems;
using Content.Server.Power.Components;
using Content.Shared.Doors.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Doors;

[TestFixture]
[TestOf(typeof(FirelockComponent))]
public sealed class FirelockTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: FirelockTestDummy
  components:
  - type: Door
  - type: Firelock
  - type: ApcPowerReceiver
    needsPower: false
  - type: Physics
    bodyType: Static
";

    [Test]
    public async Task PlayerOpenedFirelockIgnoresEmergencyPressureStop()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.ResolveDependency<IEntityManager>();
        var doors = entityManager.System<DoorSystem>();
        var firelocks = entityManager.System<FirelockSystem>();

        EntityUid firelock = default;
        EntityUid user = default;
        DoorComponent door = null!;
        FirelockComponent firelockComponent = null!;

        await server.WaitAssertion(() =>
        {
            user = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
            firelock = entityManager.SpawnEntity("FirelockTestDummy", MapCoordinates.Nullspace);
            door = entityManager.GetComponent<DoorComponent>(firelock);
            firelockComponent = entityManager.GetComponent<FirelockComponent>(firelock);
            entityManager.GetComponent<ApcPowerReceiverComponent>(firelock).Powered = true;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(doors.TryOpen(firelock, door, user), Is.True);
            Assert.That(firelockComponent.PlayerHeldOpen, Is.True);
        });

        await PoolManager.WaitUntil(server, () => door.State == DoorState.Open);

        await server.WaitAssertion(() =>
        {
            Assert.That(firelocks.EmergencyPressureStop(firelock, firelockComponent, door), Is.False);
            Assert.That(door.State, Is.EqualTo(DoorState.Open));
        });

        await pair.CleanReturnAsync();
    }
}
