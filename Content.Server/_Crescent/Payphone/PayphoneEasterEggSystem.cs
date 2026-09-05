using Content.Server.Chat.Managers;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Crescent.Payphone;

public sealed partial class PayphoneEasterEggSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private static readonly string[] Lines =
    {
        "payphone-easter-egg-calling",
        "payphone-easter-egg-calling-again",
        "payphone-easter-egg-calling-again",
        "payphone-easter-egg-calling-still",
        "payphone-easter-egg-ocean",
        "payphone-easter-egg-hello",
        "payphone-easter-egg-stunned",
        "payphone-easter-egg-beautiful",
        "payphone-easter-egg-recognition",
        "payphone-easter-egg-revolutionary",
        "payphone-easter-egg-drunk",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PayphoneEasterEggComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<PayphoneEasterEggComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnActivate(Entity<PayphoneEasterEggComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out _))
            return;

        args.Handled = true;
        var phone = ent.Comp;
        if (phone.NextLine >= 0 || Exists(phone.VoiceStream) || _timing.CurTime < phone.NextAttempt)
            return;

        var memoryOwner = _mind.TryGetMind(args.User, out var mind, out _) ? mind : args.User;
        if (HasComp<PayphoneMemoryComponent>(memoryOwner))
        {
            phone.NextAttempt = _timing.CurTime + phone.AttemptInterval;
            _popup.PopupEntity(Loc.GetString("payphone-easter-egg-already-called"), ent, args.User);
            return;
        }

        // Attempts belong to the person at the receiver, not to a crowd clicking it.
        if (phone.Caller != args.User)
        {
            phone.Caller = args.User;
            phone.Attempts = 0;
        }

        phone.NextAttempt = _timing.CurTime + phone.AttemptInterval;
        phone.Attempts++;
        if (phone.Attempts < phone.RequiredAttempts)
        {
            _popup.PopupEntity(Loc.GetString("payphone-easter-egg-no-answer"), ent, args.User);
            return;
        }

        EnsureComp<PayphoneMemoryComponent>(memoryOwner);
        phone.NextLine = 0;
        phone.NextLineTime = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<PayphoneEasterEggComponent>();
        while (query.MoveNext(out var uid, out var phone))
        {
            if (phone.Caller is not { } caller)
                continue;

            if (!Exists(caller) ||
                !TryComp<ActorComponent>(caller, out var actor) ||
                !_interaction.InRangeUnobstructed(caller, uid))
            {
                Reset(phone);
                continue;
            }

            if (phone.NextLine < 0 || _timing.CurTime < phone.NextLineTime)
                continue;

            var line = Lines[phone.NextLine];
            _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString(line));
            switch (line)
            {
                case "payphone-easter-egg-calling":
                    phone.CallingStream = _audio.PlayGlobal(phone.CallingSound, actor.PlayerSession)?.Entity;
                    break;
                case "payphone-easter-egg-ocean":
                    phone.CallingStream = _audio.Stop(phone.CallingStream);
                    break;
                case "payphone-easter-egg-stunned":
                    _stun.TryStun(caller, phone.StunDuration, false);
                    break;
            }

            if (phone.NextLine == Lines.Length - 1)
            {
                phone.VoiceStream = _audio.PlayGlobal(phone.FinalLineSound, actor.PlayerSession)?.Entity;
                phone.NextLine = -1;
                phone.Attempts = 0;
                continue;
            }

            // Schedule from now so a delayed tick cannot dump several lines at once.
            phone.NextLine++;
            phone.NextLineTime = _timing.CurTime + phone.LineInterval;
        }
    }

    private void OnShutdown(Entity<PayphoneEasterEggComponent> ent, ref ComponentShutdown args)
    {
        Reset(ent.Comp);
    }

    private void Reset(PayphoneEasterEggComponent phone)
    {
        phone.CallingStream = _audio.Stop(phone.CallingStream);
        phone.VoiceStream = _audio.Stop(phone.VoiceStream);
        phone.Caller = null;
        phone.Attempts = 0;
        phone.NextLine = -1;
    }
}
