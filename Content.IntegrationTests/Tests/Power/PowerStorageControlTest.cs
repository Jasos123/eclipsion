using Content.Server.Power.Components;
using Content.Shared.Power;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Power;

[TestFixture]
public sealed class PowerStorageControlTest
{
    [Test]
    public async Task LimitsAndSwitchesAreServerValidated()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var smes = entMan.SpawnEntity("SMESBasic", map.GridCoords);
            var control = entMan.GetComponent<PowerStorageControlComponent>(smes);
            var battery = entMan.GetComponent<PowerNetworkBatteryComponent>(smes);

            Assert.Multiple(() =>
            {
                Assert.That(control.MaxInputLimit, Is.EqualTo(25_000f));
                Assert.That(control.MaxOutputLimit, Is.EqualTo(750_000f));
            });

            var invalidInput = new PowerStorageSetInputLimitMessage(float.PositiveInfinity);
            entMan.EventBus.RaiseLocalEvent(smes, invalidInput);
            Assert.That(battery.MaxChargeRate, Is.EqualTo(25_000f));

            var oversizedInput = new PowerStorageSetInputLimitMessage(100_000f);
            entMan.EventBus.RaiseLocalEvent(smes, oversizedInput);
            Assert.That(battery.MaxChargeRate, Is.EqualTo(control.MaxInputLimit));

            var negativeOutput = new PowerStorageSetOutputLimitMessage(-1f);
            entMan.EventBus.RaiseLocalEvent(smes, negativeOutput);
            Assert.That(battery.MaxSupply, Is.Zero);

            var disableInput = new PowerStorageSetInputEnabledMessage(false);
            entMan.EventBus.RaiseLocalEvent(smes, disableInput);
            var disableOutput = new PowerStorageSetOutputEnabledMessage(false);
            entMan.EventBus.RaiseLocalEvent(smes, disableOutput);

            Assert.Multiple(() =>
            {
                Assert.That(battery.CanCharge, Is.False);
                Assert.That(battery.CanDischarge, Is.False);
            });

            var substation = entMan.SpawnEntity("SubstationBasic", map.GridCoords);
            var substationControl = entMan.GetComponent<PowerStorageControlComponent>(substation);

            Assert.Multiple(() =>
            {
                Assert.That(substationControl.MaxInputLimit, Is.EqualTo(5_000f));
                Assert.That(substationControl.MaxOutputLimit, Is.EqualTo(150_000f));
            });

            entMan.DeleteEntity(smes);
            entMan.DeleteEntity(substation);
        });

        await pair.CleanReturnAsync();
    }
}
