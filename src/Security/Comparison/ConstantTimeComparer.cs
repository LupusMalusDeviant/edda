using System.Security.Cryptography;
using System.Text;

namespace Edda.Security.Comparison;

/// <summary>
/// Provides constant-time equality comparison for secret strings such as bearer tokens.
/// <para>
/// A naive <see cref="string.Equals(string, string, System.StringComparison)"/> short-circuits at
/// the first differing character, which leaks — through response timing — how many leading
/// characters of the expected secret a candidate matched. This helper first hashes both inputs to a
/// fixed length and then compares the digests with
/// <see cref="CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>.
/// Because the digests are always the same length, the comparison branches on neither the content
/// nor the length of the expected secret, closing the timing side channel that a plain string
/// comparison — or a length-check that returns early — would leave open.
/// </para>
/// </summary>
public static class ConstantTimeComparer
{
    /// <summary>
    /// Compares two secret strings for equality without leaking, through timing, how much of the
    /// expected value a candidate matched or how long the expected value is.
    /// </summary>
    /// <param name="left">The first value, for example the candidate token supplied by the caller.</param>
    /// <param name="right">The second value, for example the configured secret token.</param>
    /// <returns>
    /// <see langword="true"/> if both values are non-<see langword="null"/> and byte-for-byte equal
    /// in their UTF-8 representation; otherwise <see langword="false"/>. A <see langword="null"/> on
    /// either side always yields <see langword="false"/>, so a missing secret never authenticates a
    /// missing candidate.
    /// </returns>
    public static bool AreEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        Span<byte> leftHash = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> rightHash = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.UTF8.GetBytes(left), leftHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(right), rightHash);

        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
