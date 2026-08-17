using System.Net;
using System.Security.Cryptography;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Generates TOTP codes the way a phone authenticator app does.
///
/// This exists because Identity cannot produce one. AuthenticatorTokenProvider
/// implements only the validating half — its GenerateAsync returns an empty
/// string, since in the real flow the code comes from the user's phone, not
/// from the server. A test that called GenerateTwoFactorTokenAsync would
/// therefore be verifying "" and would pass or fail for reasons unrelated to
/// TOTP.
///
/// Implementing the client side here is the stronger test anyway: it proves
/// that a code computed independently, from nothing but the shared key and the
/// clock, is accepted by the server. That is exactly the claim being made when
/// we say Google Authenticator will work.
///
/// RFC 6238 with the parameters Identity uses and the enrolment page pins:
/// HMAC-SHA1, 30-second step counted from the Unix epoch, 6 digits.
/// </summary>
public static class TotpCalculator
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int StepSeconds = 30;

    /// <param name="base32Key">The key exactly as GetAuthenticatorKeyAsync returns it.</param>
    /// <param name="stepOffset">
    /// Shifts the time window. Identity accepts codes from two steps either
    /// side of now, so an offset of 0 or ±1 should be accepted and ±3 should
    /// not — which is what makes the window itself testable.
    /// </param>
    public static string Generate(string base32Key, long stepOffset = 0)
    {
        var key = FromBase32(base32Key);

        var unixSeconds = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        var step = unixSeconds / StepSeconds + stepOffset;

        // Big-endian, which is what makes this interoperable at all.
        var stepBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(step));

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(stepBytes);

        // Dynamic truncation, RFC 4226 section 5.3.
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                   | ((hash[offset + 1] & 0xff) << 16)
                   | ((hash[offset + 2] & 0xff) << 8)
                   | (hash[offset + 3] & 0xff);

        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// RFC 4648 base32, the alphabet Identity's own Base32 helper uses. Padding
    /// is tolerated but Identity never emits it.
    /// </summary>
    private static byte[] FromBase32(string input)
    {
        var value = 0;
        var bits = 0;
        var output = new List<byte>(input.Length * 5 / 8);

        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0) continue;   // spaces from the formatted display key

            value = (value << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
