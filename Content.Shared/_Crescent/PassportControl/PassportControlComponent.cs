using System.Text;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.PassportControl;

/// <summary>
/// Holds the most recent document submitted to a linked passport control computer.
/// </summary>
[RegisterComponent]
public sealed partial class PassportReaderComponent : Component
{
    public const string DataPort = "PassportScanSender";

    [ViewVariables]
    public EntityUid? LastPassport;

    [ViewVariables]
    public EntityUid? LastUser;
}

/// <summary>
/// A fixed biometric reader. The transient sample stays server-side and is delivered to a linked
/// passport control computer through device linking.
/// </summary>
[RegisterComponent]
public sealed partial class FingerprintScannerComponent : Component
{
    public const string DataPort = "FingerprintScanSender";

    [ViewVariables]
    public string? LastBiometricHash;

    [ViewVariables]
    public EntityUid? LastUser;

    [DataField]
    public SoundSpecifier ScanSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg");
}

/// <summary>
/// Aggregates a passport reader and fingerprint scanner into one verification session.
/// </summary>
[RegisterComponent]
public sealed partial class PassportControlConsoleComponent : Component
{
    public const string PassportPort = "PassportScanReceiver";
    public const string FingerprintPort = "FingerprintScanReceiver";

    [ViewVariables]
    public PassportControlStatus Status;

    [ViewVariables]
    public bool HasPassport;

    [ViewVariables]
    public bool PassportValid;

    [ViewVariables]
    public bool? FingerprintMatched;

    [ViewVariables]
    public string PassportName = string.Empty;

    [ViewVariables]
    public string PassportId = string.Empty;

    [ViewVariables]
    public string PendingBiometricHash = string.Empty;
}

[Serializable, NetSerializable]
public enum PassportControlStatus : byte
{
    Idle,
    WaitingForFingerprint,
    Verified,
    PassportInvalid,
    FingerprintMismatch,
    BiometricUnavailable,
}

[Serializable, NetSerializable]
public enum PassportControlUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class PassportControlResetMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PassportControlBoundUserInterfaceState(
    PassportControlStatus status,
    bool passportReaderConnected,
    bool fingerprintScannerConnected,
    bool hasPassport,
    bool passportValid,
    bool? fingerprintMatched,
    string passportName,
    string passportId) : BoundUserInterfaceState
{
    public PassportControlStatus Status { get; } = status;
    public bool PassportReaderConnected { get; } = passportReaderConnected;
    public bool FingerprintScannerConnected { get; } = fingerprintScannerConnected;
    public bool HasPassport { get; } = hasPassport;
    public bool PassportValid { get; } = passportValid;
    public bool? FingerprintMatched { get; } = fingerprintMatched;
    public string PassportName { get; } = passportName;
    public string PassportId { get; } = passportId;
}

/// <summary>
/// Creates a non-reversible machine-readable template without storing a forensic fingerprint on
/// the passport or exposing it to clients.
/// </summary>
public static class PassportBiometrics
{
    private static readonly uint[] Sha256RoundConstants =
    [
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
    ];

    public static string HashFingerprint(string fingerprint)
    {
        // Content assemblies cannot call System.Security.Cryptography under the game sandbox. Keep the
        // biometric template one-way by implementing the small, deterministic SHA-256 transform locally.
        var input = Encoding.UTF8.GetBytes(fingerprint);
        var paddedLength = ((input.Length + 9 + 63) / 64) * 64;
        var data = new byte[paddedLength];
        Array.Copy(input, data, input.Length);
        data[input.Length] = 0x80;

        var bitLength = (ulong) input.Length * 8;
        for (var i = 0; i < 8; i++)
            data[data.Length - 1 - i] = (byte) (bitLength >> (i * 8));

        var hash = new uint[]
        {
            0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
            0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
        };
        var schedule = new uint[64];

        unchecked
        {
            for (var chunk = 0; chunk < data.Length; chunk += 64)
            {
                for (var i = 0; i < 16; i++)
                {
                    var offset = chunk + i * 4;
                    schedule[i] = (uint) data[offset] << 24
                        | (uint) data[offset + 1] << 16
                        | (uint) data[offset + 2] << 8
                        | data[offset + 3];
                }

                for (var i = 16; i < schedule.Length; i++)
                {
                    var s0 = RotateRight(schedule[i - 15], 7)
                        ^ RotateRight(schedule[i - 15], 18)
                        ^ schedule[i - 15] >> 3;
                    var s1 = RotateRight(schedule[i - 2], 17)
                        ^ RotateRight(schedule[i - 2], 19)
                        ^ schedule[i - 2] >> 10;
                    schedule[i] = schedule[i - 16] + s0 + schedule[i - 7] + s1;
                }

                var a = hash[0];
                var b = hash[1];
                var c = hash[2];
                var d = hash[3];
                var e = hash[4];
                var f = hash[5];
                var g = hash[6];
                var h = hash[7];

                for (var i = 0; i < schedule.Length; i++)
                {
                    var sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                    var choice = (e & f) ^ (~e & g);
                    var temp1 = h + sum1 + choice + Sha256RoundConstants[i] + schedule[i];
                    var sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                    var majority = (a & b) ^ (a & c) ^ (b & c);
                    var temp2 = sum0 + majority;

                    h = g;
                    g = f;
                    f = e;
                    e = d + temp1;
                    d = c;
                    c = b;
                    b = a;
                    a = temp1 + temp2;
                }

                hash[0] += a;
                hash[1] += b;
                hash[2] += c;
                hash[3] += d;
                hash[4] += e;
                hash[5] += f;
                hash[6] += g;
                hash[7] += h;
            }
        }

        var result = new StringBuilder(64);
        foreach (var word in hash)
            result.Append(word.ToString("X8"));

        return result.ToString();
    }

    public static bool Matches(string expectedHash, string scannedHash)
    {
        if (expectedHash.Length != 64 || scannedHash.Length != 64)
            return false;

        var difference = 0;
        for (var i = 0; i < expectedHash.Length; i++)
        {
            var expected = NormalizeHex(expectedHash[i]);
            var scanned = NormalizeHex(scannedHash[i]);
            if (expected < 0 || scanned < 0)
                return false;

            difference |= expected ^ scanned;
        }

        return difference == 0;
    }

    private static uint RotateRight(uint value, int offset)
    {
        return value >> offset | value << (32 - offset);
    }

    private static int NormalizeHex(char value)
    {
        if (value is >= '0' and <= '9')
            return value - '0';

        if (value is >= 'A' and <= 'F')
            return value - 'A' + 10;

        if (value is >= 'a' and <= 'f')
            return value - 'a' + 10;

        return -1;
    }

    public static PassportControlStatus DetermineStatus(
        bool passportValid,
        string expectedBiometricHash,
        string scannedBiometricHash)
    {
        if (!passportValid)
            return PassportControlStatus.PassportInvalid;

        if (string.IsNullOrEmpty(expectedBiometricHash) || string.IsNullOrEmpty(scannedBiometricHash))
            return PassportControlStatus.BiometricUnavailable;

        var matched = Matches(expectedBiometricHash, scannedBiometricHash);
        return matched
            ? PassportControlStatus.Verified
            : PassportControlStatus.FingerprintMismatch;
    }
}
