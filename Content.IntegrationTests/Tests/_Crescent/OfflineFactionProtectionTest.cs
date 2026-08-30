using Content.Server._Crescent.Economy;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
[TestOf(typeof(OfflineFactionProtectionSystem))]
public sealed class OfflineFactionProtectionTest
{
    [Test]
    public async Task ProtectsAnyNamedFactionByDefault()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var protection = pair.Server.System<OfflineFactionProtectionSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(protection.IsProtected("DSM"), Is.True);
                Assert.That(protection.IsProtected("SRM"), Is.True);
                Assert.That(protection.IsProtected("TAP"), Is.True);
                Assert.That(protection.IsProtected("TSP"), Is.True);
                Assert.That(protection.IsProtected(string.Empty), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void RequiresTwoDistinctRoundPlayers()
    {
        var now = TimeSpan.FromHours(5);

        Assert.Multiple(() =>
        {
            Assert.That(OfflineFactionProtectionSystem.ShouldProtect(0, now, now), Is.True);
            Assert.That(OfflineFactionProtectionSystem.ShouldProtect(1, now, now), Is.True);
            Assert.That(OfflineFactionProtectionSystem.ShouldProtect(2, now, now), Is.False);
        });
    }

    [Test]
    public void ProtectsAfterOneHourWithoutActivity()
    {
        var now = TimeSpan.FromHours(5);

        Assert.Multiple(() =>
        {
            Assert.That(OfflineFactionProtectionSystem.ShouldProtect(2, null, now), Is.True);
            Assert.That(OfflineFactionProtectionSystem.ShouldProtect(2, now - TimeSpan.FromHours(1), now), Is.False);
            Assert.That(
                OfflineFactionProtectionSystem.ShouldProtect(
                    2,
                    now - TimeSpan.FromHours(1) - TimeSpan.FromTicks(1),
                    now),
                Is.True);
        });
    }
}
