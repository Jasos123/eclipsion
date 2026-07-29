using Content.Server.Emp;
using Content.Server.Power.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Emp;
using Content.Shared.Mech.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Mech;

[TestFixture]
public sealed class MechEmpTest
{
    [Test]
    public async Task EmpDisablesMechAndSynchronizesBatteryCharge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var systemManager = server.ResolveDependency<IEntitySystemManager>();
        var actionBlocker = systemManager.GetEntitySystem<ActionBlockerSystem>();
        var emp = systemManager.GetEntitySystem<EmpSystem>();
        var transform = systemManager.GetEntitySystem<SharedTransformSystem>();

        EntityUid mech = default;
        EntityUid battery = default;

        await server.WaitPost(() =>
        {
            mech = entityManager.SpawnEntity("MechDSMCarrionBattery", testMap.GridCoords);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var mechComponent = entityManager.GetComponent<MechComponent>(mech);
            battery = mechComponent.BatterySlot.ContainedEntity!.Value;
            var batteryComponent = entityManager.GetComponent<BatteryComponent>(battery);

            Assert.Multiple(() =>
            {
                Assert.That(batteryComponent.CurrentCharge, Is.EqualTo(7500f));
                Assert.That(mechComponent.Energy.Float(), Is.EqualTo(7500f));
                Assert.That(actionBlocker.CanMove(mech), Is.True);
            });
        });

        await server.WaitPost(() =>
        {
            var coordinates = transform.GetMapCoordinates(mech);
            emp.EmpPulse(coordinates, 1f, 5000f, 1f);
        });

        await server.WaitAssertion(() =>
        {
            var mechComponent = entityManager.GetComponent<MechComponent>(mech);
            var batteryComponent = entityManager.GetComponent<BatteryComponent>(battery);

            Assert.Multiple(() =>
            {
                Assert.That(batteryComponent.CurrentCharge, Is.EqualTo(2500f));
                Assert.That(mechComponent.Energy.Float(), Is.EqualTo(2500f));
                Assert.That(entityManager.HasComponent<EmpDisabledComponent>(mech), Is.True);
                Assert.That(entityManager.HasComponent<EmpDisabledComponent>(battery), Is.True);
                Assert.That(actionBlocker.CanMove(mech), Is.False);
            });
        });

        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entityManager.HasComponent<EmpDisabledComponent>(mech), Is.False);
                Assert.That(entityManager.HasComponent<EmpDisabledComponent>(battery), Is.False);
                Assert.That(actionBlocker.CanMove(mech), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }
}
