using Content.Server.Administration.Logs;
using Content.Server.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server._Crescent.Diplomacy;
using Content.Shared._Crescent.Diplomacy;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared.Cargo.Cartridges;
using Content.Shared.Cargo.Components;
using Content.Shared.Database;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Economy;

/// <summary>
/// Seizes shares a player holds in a company belonging to a faction their own faction has just gone to
/// war with.
///
/// You cannot stay invested in the enemy. The moment DSM and SHI cross into war, every DSM member's
/// SHI holdings are sold out from under them and every SHI member's DSM holdings likewise — the pair is
/// symmetric, because both sides of a war are equally unwelcome shareholders.
///
/// The sale pays <see cref="StockCompanySystem.GetFloorPrice"/>, not the market price. That is the
/// point: this is a confiscation dressed as a sale, so being caught holding enemy stock when the
/// shooting starts is a real loss rather than a free exit at whatever the ticker happened to read.
/// The proceeds still reach the player's account — the position is destroyed, not the money.
///
/// Two triggers, because holdings outlive the round they were bought in (see
/// <see cref="StockPortfolioPersistenceSystem"/>): war being declared catches everyone currently
/// playing, and the portfolio restore on spawn catches anyone who was offline when it happened, or who
/// joins a later round with a war already standing.
/// </summary>
public sealed class StockWarLiquidationSystem : EntitySystem
{
    [Dependency] private readonly StockCompanySystem _stocks = default!;
    [Dependency] private readonly StockPortfolioPersistenceSystem _portfolios = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly RatDiplomacySystem _factionDiplomacy = default!;
    [Dependency] private readonly DiplomacySystem _iffDiplomacy = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionsWentToWarEvent>(OnWentToWar);
    }

    private void OnWentToWar(ref FactionsWentToWarEvent args)
    {
        // Both directions: each side is holding the other's paper.
        LiquidateHolders(args.FactionA, args.FactionB);
        LiquidateHolders(args.FactionB, args.FactionA);
    }

    /// <summary>Sells off every <paramref name="enemyFaction"/> share held by a member of <paramref name="holderFaction"/>.</summary>
    private void LiquidateHolders(string holderFaction, string enemyFaction)
    {
        if (!FactionStockCompanies.TryGetCompany(enemyFaction, out var companyId))
            return;

        var query = EntityQueryEnumerator<HullrotFactionComponent, PlayerStockPortfolioComponent>();
        while (query.MoveNext(out var uid, out var member, out var portfolio))
        {
            if (!string.Equals(member.Faction, holderFaction, StringComparison.OrdinalIgnoreCase))
                continue;

            Liquidate(uid, portfolio, companyId);
        }
    }

    /// <summary>
    /// Checks one mob against every war currently standing and seizes whatever it should not be holding.
    /// Called after a portfolio is restored on spawn, which is the only moment holdings bought in an
    /// earlier round — or while the player was offline — come back into reach.
    /// </summary>
    public void LiquidateEnemyHoldings(EntityUid mob, PlayerStockPortfolioComponent portfolio)
    {
        if (!TryComp<HullrotFactionComponent>(mob, out var member) || string.IsNullOrEmpty(member.Faction))
            return;

        foreach (var (faction, companyId) in FactionStockCompanies.ByFaction)
        {
            if (string.Equals(faction, member.Faction, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!portfolio.OwnedShares.ContainsKey(companyId))
                continue;

            if (AtWar(member.Faction, faction))
                Liquidate(mob, portfolio, companyId);
        }
    }

    /// <summary>
    /// Whether a trader may voluntarily buy or sell a company's shares. Neutral companies and traders
    /// without a faction are unrestricted; members of a faction cannot trade a company whose faction is
    /// currently at war with theirs.
    /// </summary>
    public bool CanTradeStock(EntityUid trader, string companyId)
    {
        if (!TryComp<HullrotFactionComponent>(trader, out var member) || string.IsNullOrEmpty(member.Faction))
            return true;

        if (!FactionStockCompanies.TryGetFaction(companyId, out var companyFaction))
            return true;

        return !AtWar(member.Faction, companyFaction);
    }

    /// <summary>
    /// War from either table counts. The console one is what players declare between themselves; the IFF
    /// one is what admins set and the only place some factions (TFSC) exist at all.
    /// </summary>
    private bool AtWar(string factionA, string factionB)
    {
        if (_factionDiplomacy.GetRelation(factionA, factionB) == FactionRelation.War)
            return true;

        return _iffDiplomacy.GetRelations(factionA, factionB) == Relations.War;
    }

    private void Liquidate(EntityUid mob, PlayerStockPortfolioComponent portfolio, string companyId)
    {
        if (!portfolio.OwnedShares.TryGetValue(companyId, out var shares) || shares <= 0)
            return;

        var company = _stocks.GetCompany(companyId);
        if (company == null)
            return;

        var pricePerShare = _stocks.GetFloorPrice(company);
        var payout = (int) Math.Round(pricePerShare * shares);

        // Pay first. If the money cannot land the position stays put — seizing the shares and failing to
        // deposit would delete the player's money outright, which is worse than not enforcing the rule.
        if (payout > 0 && !_bank.TryBankDeposit(mob, payout))
            return;

        portfolio.OwnedShares.Remove(companyId);

        portfolio.TradeHistory.Add(new StockTradeRecord(companyId, shares, pricePerShare, false, _timing.CurTime));
        while (portfolio.TradeHistory.Count > PlayerStockPortfolioComponent.MaxTradeHistory)
            portfolio.TradeHistory.RemoveAt(0);

        Dirty(mob, portfolio);
        _portfolios.Store(mob, portfolio);

        var companyName = Loc.GetString(companyId);

        if (_players.TryGetSessionByEntity(mob, out var session))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("stock-war-liquidation",
                ("company", companyName),
                ("shares", shares),
                ("price", (int) Math.Round(pricePerShare)),
                ("total", payout)));
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(mob):player} was liquidated out of {shares} {companyName} shares for {payout} cr (war)");
    }
}
