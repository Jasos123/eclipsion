using System.Collections.Generic;
using Content.Server._Crescent.Economy;
using Content.Server.Cargo.Cartridges;
using Content.Server.Cargo.Systems;
using Content.Shared._Crescent.Diplomacy;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.Bank.Components;
using Content.Shared.Cargo.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Economy;

/// <summary>
/// Going to war with a company must close every enemy member's position in it, at the floor price, with
/// the proceeds actually reaching their account. This moves player money, so it is worth pinning down:
/// a bug that seizes shares without paying is theft, and one that pays the market price makes holding
/// enemy stock free.
/// </summary>
[TestFixture]
public sealed class StockWarLiquidationTest
{
    private const string ShiCompany = "stock-company-shi";
    private const string NcwlCompany = "stock-company-ncwl";
    private const string NeutralCompany = "stock-company-tccc";

    [Test]
    public async Task WarBlocksBuyingAndSellingEnemyStock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var systems = server.ResolveDependency<IEntitySystemManager>();
        var stocks = systems.GetEntitySystem<StockCompanySystem>();
        var market = systems.GetEntitySystem<StockMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            // DSM and NCWL are permanent enemies, so this covers a war that is already active when the
            // transaction arrives rather than only the instant a declaration event is raised.
            stocks.SetActive(NcwlCompany, true);

            var trader = entManager.SpawnEntity(null, testMap.MapCoords);
            var faction = entManager.AddComponent<HullrotFactionComponent>(trader);
            faction.Faction = "DSM";

            var bank = entManager.AddComponent<BankAccountComponent>(trader);
            bank.Balance = 1_000_000;

            var portfolio = entManager.AddComponent<PlayerStockPortfolioComponent>(trader);
            portfolio.OwnedShares[NcwlCompany] = 5;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(market.TryBuyStock(trader, NcwlCompany, 1), Is.False,
                    "Buying stock belonging to an enemy faction must be rejected while at war.");
                Assert.That(market.TrySellStock(trader, NcwlCompany, 1), Is.False,
                    "Selling stock belonging to an enemy faction must be rejected while at war.");
                Assert.That(bank.Balance, Is.EqualTo(1_000_000),
                    "Rejected enemy trades must not move money.");
                Assert.That(portfolio.OwnedShares.GetValueOrDefault(NcwlCompany), Is.EqualTo(5),
                    "Rejected enemy trades must not move shares.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WarSeizesEnemyHoldingsAtFloorPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var systems = server.ResolveDependency<IEntitySystemManager>();
        var stocks = systems.GetEntitySystem<StockCompanySystem>();

        await server.WaitAssertion(() =>
        {
            var trader = entManager.SpawnEntity(null, testMap.MapCoords);

            var faction = entManager.AddComponent<HullrotFactionComponent>(trader);
            faction.Faction = "DSM";

            var bank = entManager.AddComponent<BankAccountComponent>(trader);
            bank.Balance = 0;

            var portfolio = entManager.AddComponent<PlayerStockPortfolioComponent>(trader);
            portfolio.OwnedShares[ShiCompany] = 10;
            portfolio.OwnedShares[NeutralCompany] = 7;

            var shi = stocks.GetCompany(ShiCompany);
            Assert.That(shi, Is.Not.Null, "SHI must be a listed company for this test to mean anything.");

            var expectedPayout = (int) Math.Round(stocks.GetFloorPrice(shi!) * 10);

            // DSM and SHI cross into war. The trader is DSM, holding SHI.
            var ev = new FactionsWentToWarEvent("DSM", "SHI");
            entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(portfolio.OwnedShares.ContainsKey(ShiCompany), Is.False,
                    "Enemy holdings should have been closed out entirely.");

                Assert.That(bank.Balance, Is.EqualTo(expectedPayout),
                    "The forced sale must pay the floor price into the trader's account.");

                Assert.That(portfolio.OwnedShares.GetValueOrDefault(NeutralCompany), Is.EqualTo(7),
                    "A company nobody is at war with must be left alone.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The pair is symmetric: it is not only the declaring side that has to divest. A SHI member holding
    /// DSM stock loses it to the same war.
    /// </summary>
    [Test]
    public async Task WarSeizesHoldingsInBothDirections()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var shiTrader = entManager.SpawnEntity(null, testMap.MapCoords);

            var faction = entManager.AddComponent<HullrotFactionComponent>(shiTrader);
            faction.Faction = "SHI";

            entManager.AddComponent<BankAccountComponent>(shiTrader);

            var portfolio = entManager.AddComponent<PlayerStockPortfolioComponent>(shiTrader);
            portfolio.OwnedShares["stock-company-dsm"] = 4;

            var ev = new FactionsWentToWarEvent("DSM", "SHI");
            entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);

            Assert.That(portfolio.OwnedShares.ContainsKey("stock-company-dsm"), Is.False,
                "The other side of the war must be divested too.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Holding your own faction's stock is the normal case and must survive a war it is a party to.
    /// </summary>
    [Test]
    public async Task WarLeavesOwnFactionHoldingsAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var patriot = entManager.SpawnEntity(null, testMap.MapCoords);

            var faction = entManager.AddComponent<HullrotFactionComponent>(patriot);
            faction.Faction = "DSM";

            entManager.AddComponent<BankAccountComponent>(patriot);

            var portfolio = entManager.AddComponent<PlayerStockPortfolioComponent>(patriot);
            portfolio.OwnedShares["stock-company-dsm"] = 12;

            var ev = new FactionsWentToWarEvent("DSM", "SHI");
            entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);

            Assert.That(portfolio.OwnedShares.GetValueOrDefault("stock-company-dsm"), Is.EqualTo(12),
                "A trader's own faction's stock is not enemy stock.");
        });

        await pair.CleanReturnAsync();
    }
}
