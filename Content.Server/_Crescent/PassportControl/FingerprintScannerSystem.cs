using Content.Server.DeviceLinking.Systems;
using Content.Server.Forensics;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.PassportControl;
using Content.Shared.Audio;
using Content.Shared.DeviceLinking;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Crescent.PassportControl;

public sealed class FingerprintScannerSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FingerprintScannerComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<FingerprintScannerComponent> scanner, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!_power.IsPowered(scanner.Owner))
        {
            _popup.PopupEntity(Loc.GetString("passport-control-no-power"), scanner.Owner, args.User,
                PopupType.MediumCaution);
            return;
        }

        if (_inventory.TryGetSlotEntity(args.User, "gloves", out _))
        {
            _popup.PopupEntity(Loc.GetString("passport-control-fingerprint-gloves"), scanner.Owner, args.User,
                PopupType.MediumCaution);
            return;
        }

        if (!TryComp<FingerprintComponent>(args.User, out var fingerprint)
            || string.IsNullOrEmpty(fingerprint.Fingerprint))
        {
            _popup.PopupEntity(Loc.GetString("passport-control-fingerprint-unreadable"), scanner.Owner, args.User,
                PopupType.MediumCaution);
            return;
        }

        scanner.Comp.LastBiometricHash = PassportBiometrics.HashFingerprint(fingerprint.Fingerprint);
        scanner.Comp.LastUser = args.User;

        _audio.PlayPvs(scanner.Comp.ScanSound, scanner.Owner);
        _deviceLink.InvokePort(scanner.Owner, FingerprintScannerComponent.DataPort);

        var linked = TryComp<DeviceLinkSourceComponent>(scanner.Owner, out var source)
            && source.LinkedPorts.Any(entry =>
                HasComp<PassportControlConsoleComponent>(entry.Key)
                && entry.Value.Any(link =>
                    link.source.Id == FingerprintScannerComponent.DataPort
                    && link.sink.Id == PassportControlConsoleComponent.FingerprintPort));
        var message = linked
            ? "passport-control-fingerprint-sent"
            : "passport-control-reader-not-connected";
        _popup.PopupEntity(Loc.GetString(message), scanner.Owner, args.User,
            linked ? PopupType.Medium : PopupType.MediumCaution);
    }
}
