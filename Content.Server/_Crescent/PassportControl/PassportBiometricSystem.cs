using Content.Server.Forensics;
using Content.Shared._Crescent.PassportControl;
using Content.Shared._EE.Contractors.Components;

namespace Content.Server._Crescent.PassportControl;

/// <summary>
/// Enrols the holder's fingerprint into newly issued passports. Keeping this in a server system
/// prevents the forensic fingerprint and its derived reference from being sent to clients.
/// </summary>
public sealed class PassportBiometricSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PassportComponent, PassportIssuedEvent>(OnPassportIssued);
    }

    private void OnPassportIssued(Entity<PassportComponent> passport, ref PassportIssuedEvent args)
    {
        if (!TryComp<FingerprintComponent>(args.Holder, out var fingerprint)
            || string.IsNullOrEmpty(fingerprint.Fingerprint))
            return;

        passport.Comp.BiometricHash = PassportBiometrics.HashFingerprint(fingerprint.Fingerprint);
    }
}
