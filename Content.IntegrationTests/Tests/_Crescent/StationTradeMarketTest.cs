using Content.Server.Crescent.Dispenser;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class StationTradeMarketTest
{
    [Test]
    public void CombinedMarketEffectsCannotBreakPayoutFloor()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DispenserSystem.CalculateTradePayoutMultiplier(0.5f, 0.5f), Is.EqualTo(0.5f));
            Assert.That(DispenserSystem.CalculateTradePayoutMultiplier(0.9f, 0.95f),
                Is.EqualTo(0.855f).Within(0.0001f));
            Assert.That(DispenserSystem.CalculateTradePayoutMultiplier(float.NaN, 1f), Is.EqualTo(0.5f));
        }
    }

    [Test]
    public async Task SaturationDevaluesGraduallyAndHasAFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var marketSystem = entManager.System<StationTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var station = entManager.SpawnEntity(null, testMap.MapCoords);
            var market = entManager.AddComponent<StationTradeMarketComponent>(station);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(market.PriceDropPerSale, Is.EqualTo(0.01f));
                Assert.That(market.MinMultiplier, Is.EqualTo(0.5f));
                Assert.That(market.RecoveryRatePerSecond, Is.EqualTo(1f / 30f));
                Assert.That(marketSystem.GetPriceMultiplier(station, "TestGood"), Is.EqualTo(1f));
            }

            for (var i = 0; i < 10; i++)
                marketSystem.RecordSale(station, "TestGood");

            Assert.That(marketSystem.GetPriceMultiplier(station, "TestGood"),
                Is.EqualTo(0.9f).Within(0.0001f),
                "Ten rapid sales should reduce the local price by only 10%.");

            for (var i = 0; i < 100; i++)
                marketSystem.RecordSale(station, "TestGood");

            Assert.That(marketSystem.GetPriceMultiplier(station, "TestGood"),
                Is.EqualTo(0.5f),
                "Market saturation must not reduce an item's local price below 50%.");

            entManager.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}
