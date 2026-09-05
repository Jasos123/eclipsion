using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Bank;
using Content.Server.CartridgeLoader;
using Content.Shared._Crescent.CartridgeLoader.Cartridges;
using Content.Shared.Bank.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.CartridgeLoader.Cartridges;

/// <summary>
/// A PDA casino. Bets are debited from, and winnings credited to, the player's own
/// <see cref="BankAccountComponent"/> — the same account wages are paid into.
/// Odds live in <see cref="GamblingTables"/>; every game returns roughly 90%.
/// </summary>
public sealed class GamblingCartridgeSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>Cooldown between money-committing actions, to stop macro spam.</summary>
    private static readonly TimeSpan ActionCooldown = TimeSpan.FromSeconds(0.5);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GamblingCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<GamblingCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    private void OnUiReady(EntityUid uid, GamblingCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component, GetLoaderMob(args.Loader), null, null, false);
    }

    private void OnUiMessage(EntityUid uid, GamblingCartridgeComponent component, CartridgeMessageEvent args)
    {
        var loaderUid = GetEntity(args.LoaderUid);
        var player = args.Actor;

        string? error;
        string? toast = null;
        var toastGood = false;

        switch (args)
        {
            case GamblingSpinSlotsMessage slots:
                error = TrySpinSlots(component, player, slots.Bet, out toast, out toastGood);
                break;
            case GamblingRouletteBetMessage roulette:
                error = TrySpinRoulette(component, player, roulette, out toast, out toastGood);
                break;
            case GamblingBlackjackMessage blackjack:
                error = TryBlackjack(component, player, blackjack, out toast, out toastGood);
                break;
            default:
                return;
        }

        UpdateUiState(uid, loaderUid, component, player, error, toast, toastGood);
    }

    #region Slots

    private string? TrySpinSlots(
        GamblingCartridgeComponent component,
        EntityUid player,
        int bet,
        out string? toast,
        out bool toastGood)
    {
        toast = null;
        toastGood = false;

        if (ValidateBet(component, player, bet) is { } error)
            return error;

        if (!_bank.TryBankWithdraw(player, bet))
            return Loc.GetString("gambling-error-funds");

        StartCooldown(component);

        var reels = new[] { RollReel(), RollReel(), RollReel() };
        component.LastReels = reels;

        var multiplier = GamblingTables.SlotMultiplier(reels[0], reels[1], reels[2]);
        var payout = Pay(player, (long) bet * multiplier);

        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(player):player} bet {bet} on PDA slots [{reels[0]}/{reels[1]}/{reels[2]}] and won {payout} (net {payout - bet})");

        toast = BuildResultToast(bet, payout, out toastGood);
        return null;
    }

    private SlotSymbol RollReel()
    {
        var roll = _random.Next(GamblingTables.SlotReelWeightTotal);
        foreach (var (symbol, weight) in GamblingTables.SlotReel)
        {
            if (roll < weight)
                return symbol;

            roll -= weight;
        }

        // Only reachable if the weights stop summing to the declared total.
        return GamblingTables.SlotReel[^1].Symbol;
    }

    #endregion

    #region Roulette

    private string? TrySpinRoulette(
        GamblingCartridgeComponent component,
        EntityUid player,
        GamblingRouletteBetMessage msg,
        out string? toast,
        out bool toastGood)
    {
        toast = null;
        toastGood = false;

        if (!Enum.IsDefined(msg.Kind))
            return Loc.GetString("gambling-error-bet-invalid");

        if (msg.Kind == RouletteBetKind.Straight && msg.Number is < 1 or > 36)
            return Loc.GetString("gambling-error-number");

        if (ValidateBet(component, player, msg.Bet) is { } error)
            return error;

        if (!_bank.TryBankWithdraw(player, msg.Bet))
            return Loc.GetString("gambling-error-funds");

        StartCooldown(component);

        var pocket = _random.Next(GamblingTables.RoulettePocketCount);
        component.LastPocket = pocket;

        var won = GamblingTables.RouletteWins(msg.Kind, msg.Number, pocket);
        var payout = Pay(player, won ? (long) msg.Bet * GamblingTables.RouletteMultiplier(msg.Kind) : 0);

        var betLabel = msg.Kind == RouletteBetKind.Straight ? $"{msg.Kind} {msg.Number}" : msg.Kind.ToString();
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(player):player} bet {msg.Bet} on PDA roulette ({betLabel}), pocket {GamblingTables.PocketLabel(pocket)}, won {payout} (net {payout - msg.Bet})");

        var spun = Loc.GetString("gambling-roulette-result",
            ("pocket", GamblingTables.PocketLabel(pocket)),
            ("color", Loc.GetString(PocketColorLoc(pocket))));

        toast = $"{spun} {BuildResultToast(msg.Bet, payout, out toastGood)}";
        return null;
    }

    private static string PocketColorLoc(int pocket)
    {
        if (GamblingTables.IsGreen(pocket))
            return "gambling-roulette-green";

        return GamblingTables.IsRed(GamblingTables.PocketNumber(pocket))
            ? "gambling-roulette-red"
            : "gambling-roulette-black";
    }

    #endregion

    #region Blackjack

    private string? TryBlackjack(
        GamblingCartridgeComponent component,
        EntityUid player,
        GamblingBlackjackMessage msg,
        out string? toast,
        out bool toastGood)
    {
        toast = null;
        toastGood = false;

        switch (msg.Action)
        {
            case BlackjackAction.Deal:
                return TryDeal(component, player, msg.Bet, out toast, out toastGood);

            case BlackjackAction.Hit:
            case BlackjackAction.Stand:
                if (!component.HandActive || component.HandOwner != player)
                    return Loc.GetString("gambling-error-no-hand");

                return msg.Action == BlackjackAction.Hit
                    ? Hit(component, player, out toast, out toastGood)
                    : Stand(component, player, out toast, out toastGood);

            default:
                return Loc.GetString("gambling-error-bet-invalid");
        }
    }

    private string? TryDeal(
        GamblingCartridgeComponent component,
        EntityUid player,
        int bet,
        out string? toast,
        out bool toastGood)
    {
        toast = null;
        toastGood = false;

        // Guard the owner against fat-fingering Deal mid-hand. A different player instead
        // forfeits the abandoned hand, whose bet was already debited, so no money is created
        // and nobody can lock the table by walking away from one.
        if (component.HandActive)
        {
            if (component.HandOwner == player)
                return Loc.GetString("gambling-error-hand-active");

            _adminLog.Add(LogType.ATMUsage, LogImpact.Medium,
                $"{ToPrettyString(player):player} forfeited an abandoned PDA blackjack hand belonging to {ToPrettyString(component.HandOwner):player} (bet {component.HandBet})");
        }

        if (ValidateBet(component, player, bet) is { } error)
            return error;

        if (!_bank.TryBankWithdraw(player, bet))
            return Loc.GetString("gambling-error-funds");

        StartCooldown(component);

        component.HandOwner = player;
        component.HandBet = bet;
        component.HandActive = true;
        component.DealerHoleHidden = true;
        component.PlayerCards = new List<GamblingCard> { Draw(), Draw() };
        component.DealerCards = new List<GamblingCard> { Draw(), Draw() };

        var playerNatural = GamblingTables.IsBlackjack(component.PlayerCards);
        var dealerNatural = GamblingTables.IsBlackjack(component.DealerCards);

        if (!playerNatural && !dealerNatural)
            return null;

        // A natural on either side ends the hand before the player ever acts.
        if (playerNatural && !dealerNatural)
        {
            // Blackjack pays 3:2; integer division floors the half credit toward the house.
            ResolveHand(component, player, (long) bet * 5 / 2, "blackjack-natural", out toast, out toastGood);
            return null;
        }

        // Dealer natural, or both natural. The dealer takes pushes, so two naturals lose too.
        var reason = playerNatural ? "blackjack-push-loss" : "blackjack-dealer-natural";
        ResolveHand(component, player, 0, reason, out toast, out toastGood);
        return null;
    }

    private string? Hit(GamblingCartridgeComponent component, EntityUid player, out string? toast, out bool toastGood)
    {
        toast = null;
        toastGood = false;

        component.PlayerCards.Add(Draw());
        var (total, _) = GamblingTables.HandTotal(component.PlayerCards);

        if (total > 21)
        {
            ResolveHand(component, player, 0, "blackjack-bust", out toast, out toastGood);
            return null;
        }

        // Standing automatically on 21 saves a pointless click; hitting it could only bust.
        if (total == 21)
            return Stand(component, player, out toast, out toastGood);

        return null;
    }

    private string? Stand(GamblingCartridgeComponent component, EntityUid player, out string? toast, out bool toastGood)
    {
        toast = null;
        toastGood = false;

        component.DealerHoleHidden = false;

        // Dealer draws to 17 and hits soft 17.
        while (true)
        {
            var (total, soft) = GamblingTables.HandTotal(component.DealerCards);
            var mustHit = total < GamblingTables.BlackjackDealerStand
                          || (total == GamblingTables.BlackjackDealerStand && soft);

            if (!mustHit)
                break;

            component.DealerCards.Add(Draw());
        }

        var playerTotal = GamblingTables.HandTotal(component.PlayerCards).Total;
        var dealerTotal = GamblingTables.HandTotal(component.DealerCards).Total;
        var bet = component.HandBet;

        if (dealerTotal > 21)
        {
            ResolveHand(component, player, (long) bet * 2, "blackjack-dealer-bust", out toast, out toastGood);
            return null;
        }

        if (playerTotal > dealerTotal)
        {
            ResolveHand(component, player, (long) bet * 2, "blackjack-win", out toast, out toastGood);
            return null;
        }

        // The dealer takes pushes, so an equal total is a loss.
        var reason = playerTotal == dealerTotal ? "blackjack-push-loss" : "blackjack-loss";
        ResolveHand(component, player, 0, reason, out toast, out toastGood);
        return null;
    }

    /// <summary>
    /// Closes out a hand, paying <paramref name="payoutRaw"/> (which already includes the stake).
    /// The cards and the hand owner are deliberately left in place so the player can still read
    /// the dealer's final hand; the next deal overwrites them.
    /// </summary>
    private void ResolveHand(
        GamblingCartridgeComponent component,
        EntityUid player,
        long payoutRaw,
        string reason,
        out string? toast,
        out bool toastGood)
    {
        var bet = component.HandBet;
        var payout = Pay(player, payoutRaw);

        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(player):player} bet {bet} on PDA blackjack ({reason}) and won {payout} (net {payout - bet})");

        component.HandActive = false;
        component.DealerHoleHidden = false;

        toast = $"{Loc.GetString($"gambling-{reason}")} {BuildResultToast(bet, payout, out toastGood)}";
    }

    /// <summary>Draws from an infinite shoe: ranks are uniform, so tens land 4/13 of the time.</summary>
    private GamblingCard Draw()
    {
        return new GamblingCard((byte) _random.Next(1, 14), (byte) _random.Next(0, 4));
    }

    #endregion

    #region Shared helpers

    private string? ValidateBet(GamblingCartridgeComponent component, EntityUid player, int bet)
    {
        if (!TryComp<BankAccountComponent>(player, out var bank))
            return Loc.GetString("gambling-error-no-account");

        if (bet <= 0)
            return Loc.GetString("gambling-error-bet-invalid");

        if (bank.Balance < bet)
            return Loc.GetString("gambling-error-funds");

        if (_timing.CurTime < component.NextAction)
            return Loc.GetString("gambling-error-cooldown");

        return null;
    }

    private void StartCooldown(GamblingCartridgeComponent component)
    {
        component.NextAction = _timing.CurTime + ActionCooldown;
    }

    /// <summary>
    /// Credits the full payout, including winnings larger than a 32-bit integer can hold.
    /// </summary>
    private long Pay(EntityUid player, long payoutRaw)
    {
        var payout = Math.Max(payoutRaw, 0);
        if (payout <= 0)
            return 0;

        if (_bank.TryBankDeposit(player, payout))
            return payout;

        _adminLog.Add(LogType.ATMUsage, LogImpact.High,
            $"Failed to pay {payout} of PDA gambling winnings to {ToPrettyString(player):player}");
        return 0;
    }

    private string BuildResultToast(int bet, long payout, out bool good)
    {
        var net = (long) payout - bet;
        good = net > 0;

        if (net > 0)
            return Loc.GetString("gambling-result-win", ("net", net));

        return net == 0
            ? Loc.GetString("gambling-result-push")
            : Loc.GetString("gambling-result-loss", ("net", -net));
    }

    /// <summary>
    /// Finds the mob currently holding this loader (PDA), walking up transform parents when
    /// the PDA is nested in pockets or bags.
    /// </summary>
    private EntityUid? GetLoaderMob(EntityUid loaderUid)
    {
        var uid = loaderUid;
        for (var i = 0; i < 32; i++)
        {
            if (TryComp<ActorComponent>(uid, out var actor) &&
                actor.PlayerSession?.AttachedEntity == uid &&
                HasComp<MobStateComponent>(uid))
                return uid;

            var parent = Transform(uid).ParentUid;
            if (!parent.IsValid())
                break;

            uid = parent;
        }

        return null;
    }

    private void UpdateUiState(
        EntityUid cartridgeUid,
        EntityUid loaderUid,
        GamblingCartridgeComponent? component,
        EntityUid? player,
        string? error,
        string? toast,
        bool toastGood)
    {
        if (!Resolve(cartridgeUid, ref component))
            return;

        long balance = 0;
        if (player != null && TryComp<BankAccountComponent>(player.Value, out var bank))
            balance = bank.Balance;

        // Only the owner of the hand sees its cards, so passing the PDA around can't leak
        // the dealer's hole card.
        var showCards = player != null && component.HandOwner == player && component.PlayerCards.Count > 0;
        var handActive = showCards && component.HandActive;

        var playerCards = showCards ? component.PlayerCards.ToList() : new List<GamblingCard>();
        var dealerCards = showCards
            ? (component.DealerHoleHidden ? component.DealerCards.Take(1).ToList() : component.DealerCards.ToList())
            : new List<GamblingCard>();

        var state = new GamblingUiState(
            balance,
            error,
            error != null ? null : toast,
            toastGood,
            component.LastReels,
            component.LastPocket,
            handActive,
            playerCards,
            dealerCards,
            handActive && component.DealerHoleHidden,
            handActive ? component.HandBet : 0);

        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    #endregion
}
