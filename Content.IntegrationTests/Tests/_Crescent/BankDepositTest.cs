using Content.Server.Bank;
using Content.Shared.Bank.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Crescent;

[TestFixture]
public sealed class BankDepositTest
{
    [Test]
    public async Task DepositsPreserveLargePayoutsAndRejectOverflow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var account = entMan.AddComponent<BankAccountComponent>(uid);
            var bank = entMan.System<BankSystem>();
            var jackpot = (long) int.MaxValue * 250;

            Assert.That(bank.TryBankDeposit(uid, jackpot), Is.True);
            Assert.That(account.Balance, Is.EqualTo(jackpot));

            Assert.That(bank.TryBankDeposit(uid, 0), Is.False);
            Assert.That(bank.TryBankDeposit(uid, -1), Is.False);
            Assert.That(account.Balance, Is.EqualTo(jackpot));

            Assert.That(bank.TrySetBankBalance(uid, long.MaxValue - 1), Is.True);
            Assert.That(bank.TryBankDeposit(uid, 1), Is.True);
            Assert.That(account.Balance, Is.EqualTo(long.MaxValue));
            Assert.That(bank.TryBankDeposit(uid, 1), Is.False);
            Assert.That(account.Balance, Is.EqualTo(long.MaxValue));

            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }
}
