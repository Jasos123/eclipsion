using Content.Server.DeviceLinking.Events;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.PassportControl;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._EE.Contractors.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;

namespace Content.Server._Crescent.PassportControl;

public sealed class PassportControlSystem : EntitySystem
{
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PassportControlConsoleComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<PassportControlConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<PassportControlConsoleComponent, PassportControlResetMessage>(OnResetMessage);
        SubscribeLocalEvent<PassportControlConsoleComponent, NewLinkEvent>(OnLinkChanged);
        SubscribeLocalEvent<PassportControlConsoleComponent, PortDisconnectedEvent>(OnLinkChanged);
    }

    private void OnSignalReceived(Entity<PassportControlConsoleComponent> console, ref SignalReceivedEvent args)
    {
        if (!_power.IsPowered(console.Owner) || args.Trigger is not { } source)
            return;

        switch (args.Port)
        {
            case PassportControlConsoleComponent.PassportPort:
                ReceivePassport(console, source);
                break;
            case PassportControlConsoleComponent.FingerprintPort:
                ReceiveFingerprint(console, source);
                break;
        }
    }

    private void ReceivePassport(Entity<PassportControlConsoleComponent> console, EntityUid source)
    {
        if (!TryComp<PassportReaderComponent>(source, out var reader)
            || reader.LastPassport is not { } passportUid
            || !TryComp<PassportComponent>(passportUid, out var passport))
            return;

        console.Comp.HasPassport = true;
        console.Comp.PassportName = passport.FullName;
        console.Comp.PassportId = passport.PassportId;
        console.Comp.PassportValid = SharedPassportSystem.IsPassportValid(passport);
        console.Comp.PendingBiometricHash = passport.BiometricHash;
        console.Comp.FingerprintMatched = null;
        console.Comp.Status = PassportControlStatus.WaitingForFingerprint;

        UpdateUi(console);

        if (reader.LastUser is { } user)
        {
            _popup.PopupEntity(Loc.GetString("passport-control-passport-received"), console.Owner, user,
                PopupType.Medium);
        }
    }

    private void ReceiveFingerprint(Entity<PassportControlConsoleComponent> console, EntityUid source)
    {
        if (!TryComp<FingerprintScannerComponent>(source, out var scanner)
            || string.IsNullOrEmpty(scanner.LastBiometricHash))
            return;

        if (!console.Comp.HasPassport)
        {
            if (scanner.LastUser is { } noPassportUser)
            {
                _popup.PopupEntity(Loc.GetString("passport-control-passport-first"), console.Owner, noPassportUser,
                    PopupType.MediumCaution);
            }

            UpdateUi(console);
            return;
        }

        console.Comp.FingerprintMatched = string.IsNullOrEmpty(console.Comp.PendingBiometricHash)
            ? null
            : PassportBiometrics.Matches(console.Comp.PendingBiometricHash, scanner.LastBiometricHash);
        console.Comp.Status = PassportBiometrics.DetermineStatus(
            console.Comp.PassportValid,
            console.Comp.PendingBiometricHash,
            scanner.LastBiometricHash);

        UpdateUi(console);

        if (scanner.LastUser is not { } user)
            return;

        var popup = console.Comp.Status == PassportControlStatus.Verified
            ? "passport-control-result-valid"
            : "passport-control-result-invalid";
        _popup.PopupEntity(Loc.GetString(popup), console.Owner, user,
            console.Comp.Status == PassportControlStatus.Verified
                ? PopupType.Medium
                : PopupType.MediumCaution);
    }

    private void OnUiOpened(Entity<PassportControlConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is PassportControlUiKey.Key)
            UpdateUi(console);
    }

    private void OnResetMessage(Entity<PassportControlConsoleComponent> console, ref PassportControlResetMessage args)
    {
        Reset(console.Comp);
        UpdateUi(console);
    }

    private void OnLinkChanged(Entity<PassportControlConsoleComponent> console, ref NewLinkEvent args)
    {
        UpdateUi(console);
    }

    private void OnLinkChanged(Entity<PassportControlConsoleComponent> console, ref PortDisconnectedEvent args)
    {
        UpdateUi(console);
    }

    private static void Reset(PassportControlConsoleComponent console)
    {
        console.Status = PassportControlStatus.Idle;
        console.HasPassport = false;
        console.PassportValid = false;
        console.FingerprintMatched = null;
        console.PassportName = string.Empty;
        console.PassportId = string.Empty;
        console.PendingBiometricHash = string.Empty;
    }

    private void UpdateUi(Entity<PassportControlConsoleComponent> console)
    {
        var readerConnected = false;
        var scannerConnected = false;

        if (TryComp<DeviceLinkSinkComponent>(console.Owner, out var sink))
        {
            foreach (var sourceUid in sink.LinkedSources)
            {
                if (!TryComp<DeviceLinkSourceComponent>(sourceUid, out var source))
                    continue;

                var links = _deviceLink.GetLinks(sourceUid, console.Owner, source);

                if (HasComp<PassportReaderComponent>(sourceUid)
                    && links.Any(link =>
                        link.source.Id == PassportReaderComponent.DataPort
                        && link.sink.Id == PassportControlConsoleComponent.PassportPort))
                    readerConnected = true;

                if (HasComp<FingerprintScannerComponent>(sourceUid)
                    && links.Any(link =>
                        link.source.Id == FingerprintScannerComponent.DataPort
                        && link.sink.Id == PassportControlConsoleComponent.FingerprintPort))
                    scannerConnected = true;
            }
        }

        _ui.SetUiState(console.Owner, PassportControlUiKey.Key,
            new PassportControlBoundUserInterfaceState(
                console.Comp.Status,
                readerConnected,
                scannerConnected,
                console.Comp.HasPassport,
                console.Comp.PassportValid,
                console.Comp.FingerprintMatched,
                console.Comp.PassportName,
                console.Comp.PassportId));
    }
}
