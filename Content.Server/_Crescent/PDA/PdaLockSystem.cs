using Content.Server.Mind;
using Content.Server.Popups;
using Content.Shared._Crescent.PDA;
using Content.Shared.CartridgeLoader;
using Content.Shared.Ghost;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Crescent.PDA;

/// <summary>
/// Binds a PDA to the first player who opens it and refuses its money and messaging apps to everyone
/// else. Killing a trader for their PDA no longer hands over their portfolio.
/// </summary>
/// <remarks>
/// The check is enforced where cartridge UI traffic enters the server rather than inside each app, so a
/// client that skips the "open program" step and posts a buy order straight at the loader is refused by
/// the same gate. Binding is to the mind's user id: the owner keeps their device through cloning or a
/// body transfer, and an ID card swap does not launder a stolen one.
/// </remarks>
public sealed class PdaLockSystem : EntitySystem
{
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Subscribed on the loader rather than PdaComponent: only one system may hold a given
        // component/event pair and PdaSystem already owns PdaComponent's BoundUIOpenedEvent. Every PDA is
        // a cartridge loader, so this fires on exactly the same devices.
        SubscribeLocalEvent<CartridgeLoaderComponent, BoundUIOpenedEvent>(OnPdaOpened);
        SubscribeLocalEvent<PdaComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    /// <summary>
    /// Claims an unclaimed PDA for whoever opened it. Nothing is taken from anyone: a PDA that already
    /// has an owner is left alone, and one nobody has ever opened belongs to the next person to try.
    /// </summary>
    private void OnPdaOpened(EntityUid uid, CartridgeLoaderComponent loader, BoundUIOpenedEvent args)
    {
        TryBind(uid, args.Actor);
    }

    private void TryBind(EntityUid pdaUid, EntityUid actor)
    {
        // An admin ghost poking at a PDA must not end up owning it.
        if (HasComp<GhostComponent>(actor))
            return;

        if (GetUser(actor) is not { } user)
            return;

        var comp = EnsureComp<PdaLockComponent>(pdaUid);
        if (!comp.Enabled || comp.OwnerUser != null)
            return;

        comp.OwnerUser = user;
        comp.OwnerName = Name(actor);
    }

    /// <summary>
    /// Whether <paramref name="actor"/> may run <paramref name="program"/> on <paramref name="loader"/>.
    /// Unmarked programs are open to everyone; marked ones need the device's owner.
    /// </summary>
    public bool CanUseProgram(EntityUid loader, EntityUid program, EntityUid actor)
    {
        if (!HasComp<OwnerLockedProgramComponent>(program))
            return true;

        return IsOwner(loader, actor);
    }

    /// <summary>
    /// Whether <paramref name="actor"/> owns this device. An unbound or lock-disabled PDA answers to
    /// anyone, and admin ghosts are never locked out of anything.
    /// </summary>
    public bool IsOwner(EntityUid loader, EntityUid actor)
    {
        if (!TryComp<PdaLockComponent>(loader, out var comp) || !comp.Enabled || comp.OwnerUser == null)
            return true;

        if (HasComp<GhostComponent>(actor))
            return true;

        return GetUser(actor) == comp.OwnerUser;
    }

    /// <summary>
    /// Tells <paramref name="actor"/> why the app will not open. Kept separate from
    /// <see cref="CanUseProgram"/> so a denial can be reported once at the point the player pressed
    /// something, rather than on every relayed message.
    /// </summary>
    public void PopupDenied(EntityUid loader, EntityUid actor)
    {
        var owner = CompOrNull<PdaLockComponent>(loader)?.OwnerName;

        _popup.PopupEntity(
            string.IsNullOrWhiteSpace(owner)
                ? Loc.GetString("pda-lock-denied-unknown")
                : Loc.GetString("pda-lock-denied", ("owner", owner)),
            loader,
            actor,
            PopupType.MediumCaution);
    }

    /// <summary>
    /// Lets the owner hand the device on. Without this a PDA issued to a job could never be passed to
    /// the next shift's holder, and a legitimate trade would be indistinguishable from a mugging.
    /// </summary>
    private void OnGetVerbs(EntityUid uid, PdaComponent pda, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp<PdaLockComponent>(uid, out var comp) || !comp.Enabled || comp.OwnerUser == null)
            return;

        // Only the registered owner can release the binding, otherwise the lock would be one verb click
        // away from useless.
        if (GetUser(args.User) != comp.OwnerUser)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("pda-lock-verb-clear"),
            Act = () =>
            {
                comp.OwnerUser = null;
                comp.OwnerName = null;
                _popup.PopupEntity(Loc.GetString("pda-lock-cleared"), uid, user);
            },
        });
    }

    private NetUserId? GetUser(EntityUid actor)
    {
        if (_mind.TryGetMind(actor, out _, out var mind) && mind.UserId is { } userId)
            return userId;

        return CompOrNull<ActorComponent>(actor)?.PlayerSession.UserId;
    }
}
