using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Bank;
using Content.Server.Crescent.Dispenser;
using Content.Shared._Crescent.HullrotFaction;
using Content.Shared._Crescent.Payment;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Payment;

/// <summary>
/// Drives the payroll console UI: lists the faction's members with their standing salaries, and
/// applies changes made from the console.
/// </summary>
public sealed class PaymentConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StationTradeMarketSystem _market = default!;
    [Dependency] private readonly FactionPayrollSystem _payroll = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const int MaxBonus = 1_000_000;
    private const int MaxReasonLength = 128;

    /// <summary>How often an open console re-reads the roster and the treasury balance.</summary>
    private const float RefreshInterval = 3f;

    private float _sinceRefresh;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PaymentConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<PaymentConsoleComponent, PaymentSetSalaryMessage>(OnSetSalary);
        SubscribeLocalEvent<PaymentConsoleComponent, PaymentClearSalaryMessage>(OnClearSalary);
        SubscribeLocalEvent<PaymentConsoleComponent, PaymentBonusMessage>(OnBonus);
    }

    private void OnUiOpened(Entity<PaymentConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, PaymentConsoleUiKey.Key))
            return;

        UpdateUi(ent);
    }

    /// <summary>
    /// Keeps an open console current. It only ever redrew in response to its own buttons, so a member
    /// who joined the faction, disconnected or died after it was opened never appeared or changed, and
    /// the treasury figure sat frozen while payroll and purchases moved it.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _sinceRefresh += frameTime;
        if (_sinceRefresh < RefreshInterval)
            return;

        _sinceRefresh = 0f;

        var query = EntityQueryEnumerator<PaymentConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, PaymentConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    /// <summary>
    /// Re-checks access on every message. The UI gate can be bypassed by a crafted message, so it is
    /// never the only check — same stance as the station ATM.
    /// </summary>
    private bool CanUse(Entity<PaymentConsoleComponent> ent, EntityUid actor)
    {
        if (_access.IsAllowed(actor, ent))
            return true;

        _popup.PopupEntity(Loc.GetString("payment-console-access-denied"), ent, actor);
        return false;
    }

    private void OnSetSalary(Entity<PaymentConsoleComponent> ent, ref PaymentSetSalaryMessage args)
    {
        if (!CanUse(ent, args.Actor) || string.IsNullOrEmpty(ent.Comp.Faction))
            return;

        _payroll.SetSalary(ent.Comp.Faction, args.User, args.SalaryPerHour);

        _adminLogger.Add(LogType.ATMUsage, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set {ent.Comp.Faction} salary for {args.User} to {args.SalaryPerHour}/hr");

        UpdateUi(ent);
    }

    private void OnClearSalary(Entity<PaymentConsoleComponent> ent, ref PaymentClearSalaryMessage args)
    {
        if (!CanUse(ent, args.Actor) || string.IsNullOrEmpty(ent.Comp.Faction))
            return;

        _payroll.ClearSalary(ent.Comp.Faction, args.User);

        _adminLogger.Add(LogType.ATMUsage, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} removed {args.User} from {ent.Comp.Faction} payroll");

        UpdateUi(ent);
    }

    /// <summary>One-off payment from the treasury, outside the salary schedule.</summary>
    private void OnBonus(Entity<PaymentConsoleComponent> ent, ref PaymentBonusMessage args)
    {
        if (!CanUse(ent, args.Actor) || string.IsNullOrEmpty(ent.Comp.Faction))
            return;

        var reason = args.Reason.Trim();
        if (args.Amount <= 0 || args.Amount > MaxBonus || string.IsNullOrEmpty(reason))
            return;

        if (reason.Length > MaxReasonLength)
            reason = reason[..MaxReasonLength];

        var now = _timing.CurTime;
        if (ent.Comp.LastBonus is { } last && now - last < ent.Comp.BonusCooldown)
        {
            var secondsLeft = Math.Max(1, (int) Math.Ceiling((ent.Comp.BonusCooldown - (now - last)).TotalSeconds));
            _popup.PopupEntity(Loc.GetString("payment-console-bonus-cooldown", ("seconds", secondsLeft)), ent, args.Actor);
            return;
        }

        var station = _market.TryGetFactionTreasuryStation(ent.Comp.Faction);
        if (station == null)
        {
            _popup.PopupEntity(Loc.GetString("payment-console-no-treasury"), ent, args.Actor);
            return;
        }

        if (!TryFindMember(ent.Comp.Faction, args.User, out var mob, out var session))
        {
            _popup.PopupEntity(Loc.GetString("payment-console-member-gone"), ent, args.Actor);
            return;
        }

        // Same rule as the salary run: crediting a detached mob destroys the money, so refuse instead.
        if (!_payroll.IsPayable(session, mob))
        {
            _popup.PopupEntity(Loc.GetString("payment-console-member-unpayable"), ent, args.Actor);
            return;
        }

        // Identify the operator so the bonus is billed to their own per-round share of the vault.
        if (!TryComp<ActorComponent>(args.Actor, out var operatorActor))
            return;

        // Capped, not raw: the operator's bonuses and their own hand withdrawals at the vault draw on
        // one budget, so signing bonuses is no longer a way around the treasury console's limit.
        var paid = _market.TryWithdrawTreasuryCapped(
            station.Value, operatorActor.PlayerSession.UserId, args.Amount, ent.Comp.MaxPayoutFraction);

        if (paid <= 0)
        {
            _popup.PopupEntity(
                Loc.GetString("payment-console-bonus-limit",
                    ("percent", (int) MathF.Round(ent.Comp.MaxPayoutFraction * 100f))),
                ent, args.Actor);
            return;
        }

        if (!_bank.TryBankDeposit(mob, paid))
        {
            // Refund rather than plain re-add, so the failed attempt does not eat the operator's budget.
            _market.RefundTreasuryCapped(station.Value, operatorActor.PlayerSession.UserId, paid);
            _popup.PopupEntity(Loc.GetString("payment-console-member-unpayable"), ent, args.Actor);
            return;
        }

        ent.Comp.LastBonus = now;

        _adminLogger.Add(LogType.ATMUsage, LogImpact.High,
            $"{ToPrettyString(args.Actor):player} paid a {paid} bonus from {ent.Comp.Faction} treasury to {ToPrettyString(mob):player}. Reason: {reason}");

        _popup.PopupEntity(Loc.GetString("payment-console-bonus-paid", ("amount", paid)), ent, args.Actor);
        UpdateUi(ent);
    }

    private bool TryFindMember(string faction, NetUserId user, out EntityUid mob, out ICommonSession session)
    {
        var query = EntityQueryEnumerator<HullrotFactionComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var factionComp, out var actor))
        {
            if (factionComp.Faction != faction || actor.PlayerSession.UserId != user)
                continue;

            mob = uid;
            session = actor.PlayerSession;
            return true;
        }

        mob = default;
        session = default!;
        return false;
    }

    public void UpdateUi(Entity<PaymentConsoleComponent> ent)
    {
        if (!_ui.HasUi(ent, PaymentConsoleUiKey.Key))
            return;

        var faction = ent.Comp.Faction;
        var station = _market.TryGetFactionTreasuryStation(faction);

        var members = new List<PaymentMemberEntry>();
        var seen = new HashSet<NetUserId>();

        var query = EntityQueryEnumerator<HullrotFactionComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var factionComp, out var actor))
        {
            if (factionComp.Faction != faction)
                continue;

            var session = actor.PlayerSession;
            seen.Add(session.UserId);

            members.Add(new PaymentMemberEntry
            {
                User = session.UserId,
                Name = Name(uid),
                Job = GetJobTitle(uid),
                SalaryPerHour = _payroll.GetEntry(faction, session.UserId)?.SalaryPerHour ?? 0,
                Payable = _payroll.IsPayable(session, uid),
                Stale = false,
            });
        }

        // Payroll entries with nobody in the faction to match. They're inert, but command needs to see
        // them to prune them.
        foreach (var (user, entry) in _payroll.GetRoster(faction))
        {
            if (seen.Contains(user))
                continue;

            members.Add(new PaymentMemberEntry
            {
                User = user,
                Name = Loc.GetString("payment-console-member-offline"),
                Job = string.Empty,
                SalaryPerHour = entry.SalaryPerHour,
                Payable = false,
                Stale = true,
            });
        }

        members = members.OrderBy(m => m.Stale).ThenBy(m => m.Name).ToList();

        var state = new PaymentConsoleState(
            station is { } s ? _market.GetTreasury(s) : 0,
            station != null,
            members);

        _ui.SetUiState(ent.Owner, PaymentConsoleUiKey.Key, state);
    }

    private string GetJobTitle(EntityUid entity)
    {
        if (TryComp<IdCardComponent>(entity, out var idCard) && !string.IsNullOrEmpty(idCard.LocalizedJobTitle))
            return idCard.LocalizedJobTitle;

        if (_idCard.TryFindIdCard(entity, out var found) && !string.IsNullOrEmpty(found.Comp.LocalizedJobTitle))
            return found.Comp.LocalizedJobTitle;

        return Loc.GetString("payment-console-job-unknown");
    }
}
