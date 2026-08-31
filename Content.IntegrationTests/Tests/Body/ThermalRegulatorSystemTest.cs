using Content.Server.Body.Systems;
using Content.Server.Temperature.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(ThermalRegulatorSystem))]
public sealed class ThermalRegulatorSystemTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ThermalRegulatorTestDummy
  components:
  - type: Temperature
    currentTemperature: 284.15
  - type: ThermalRegulator
    shiveringHeatRegulation: 1
    normalBodyTemperature: 310.15
    thermalRegulationTemperatureThreshold: 25
";

    [Test]
    public async Task SevereColdTriggersShivering()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid uid = default;
        float initialTemperature = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            uid = entMan.SpawnEntity("ThermalRegulatorTestDummy", MapCoordinates.Nullspace);
            initialTemperature = entMan.GetComponent<TemperatureComponent>(uid).CurrentTemperature;
        });

        await server.WaitRunTicks(65);

        await server.WaitAssertion(() =>
        {
            var temperature = server.EntMan.GetComponent<TemperatureComponent>(uid);
            Assert.That(temperature.CurrentTemperature, Is.GreaterThan(initialTemperature),
                "A severely cold body should actively generate heat by shivering.");
        });

        await pair.CleanReturnAsync();
    }
}
