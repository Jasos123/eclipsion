using Content.Shared._Crescent.PassportControl;

namespace Content.IntegrationTests.Tests;

[TestFixture]
[TestOf(typeof(PassportBiometrics))]
public sealed class PassportControlTest
{
    private const string FingerprintA = "A-person-forensic-fingerprint";
    private const string FingerprintB = "another-person-forensic-fingerprint";

    [Test]
    public void FingerprintTemplateUsesSha256()
    {
        Assert.That(
            PassportBiometrics.HashFingerprint("abc"),
            Is.EqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
    }

    [Test]
    public void ValidPassportAndMatchingFingerprintAreVerified()
    {
        var enrolled = PassportBiometrics.HashFingerprint(FingerprintA);
        var scan = PassportBiometrics.HashFingerprint(FingerprintA);

        Assert.That(PassportBiometrics.DetermineStatus(true, enrolled, scan),
            Is.EqualTo(PassportControlStatus.Verified));
    }

    [Test]
    public void ValidPassportAndDifferentFingerprintAreRejected()
    {
        var enrolled = PassportBiometrics.HashFingerprint(FingerprintA);
        var scan = PassportBiometrics.HashFingerprint(FingerprintB);

        Assert.That(PassportBiometrics.DetermineStatus(true, enrolled, scan),
            Is.EqualTo(PassportControlStatus.FingerprintMismatch));
    }

    [Test]
    public void InvalidPassportIsRejectedEvenWhenFingerprintMatches()
    {
        var enrolled = PassportBiometrics.HashFingerprint(FingerprintA);

        Assert.That(PassportBiometrics.DetermineStatus(false, enrolled, enrolled),
            Is.EqualTo(PassportControlStatus.PassportInvalid));
    }

    [Test]
    public void PassportWithoutBiometricEnrollmentIsRejected()
    {
        var scan = PassportBiometrics.HashFingerprint(FingerprintA);

        Assert.That(PassportBiometrics.DetermineStatus(true, string.Empty, scan),
            Is.EqualTo(PassportControlStatus.BiometricUnavailable));
    }

    [Test]
    public void InvalidPassportTakesPriorityOverMissingBiometricEnrollment()
    {
        var scan = PassportBiometrics.HashFingerprint(FingerprintA);

        Assert.That(PassportBiometrics.DetermineStatus(false, string.Empty, scan),
            Is.EqualTo(PassportControlStatus.PassportInvalid));
    }
}
