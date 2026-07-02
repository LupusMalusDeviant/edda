using Edda.Security.Comparison;

namespace Edda.Security.Tests.Comparison;

/// <summary>
/// Unit tests for <see cref="ConstantTimeComparer"/>. These cover the behavioural contract
/// (which pairs of values are considered equal) rather than the timing property itself, which
/// cannot be asserted reliably in a unit test.
/// </summary>
public class ConstantTimeComparerTests
{
    [Fact]
    public void AreEqual_IdenticalTokens_ReturnsTrue()
    {
        ConstantTimeComparer.AreEqual("s3cret-token-value", "s3cret-token-value").Should().BeTrue();
    }

    [Fact]
    public void AreEqual_DifferentTokensOfSameLength_ReturnsFalse()
    {
        ConstantTimeComparer.AreEqual("token-aaaaaaaaaaaa", "token-bbbbbbbbbbbb").Should().BeFalse();
    }

    [Fact]
    public void AreEqual_EmptyCandidateWithConfiguredToken_ReturnsFalse()
    {
        ConstantTimeComparer.AreEqual(string.Empty, "configured-token").Should().BeFalse();
    }

    [Fact]
    public void AreEqual_CandidateOfDifferentLength_ReturnsFalse()
    {
        ConstantTimeComparer.AreEqual("short", "a-much-longer-token").Should().BeFalse();
    }

    [Fact]
    public void AreEqual_NullCandidate_ReturnsFalse()
    {
        ConstantTimeComparer.AreEqual(null, "configured-token").Should().BeFalse();
    }

    [Fact]
    public void AreEqual_NullExpected_ReturnsFalse()
    {
        ConstantTimeComparer.AreEqual("candidate-token", null).Should().BeFalse();
    }

    [Fact]
    public void AreEqual_BothNull_ReturnsFalse()
    {
        ConstantTimeComparer.AreEqual(null, null).Should().BeFalse();
    }

    [Fact]
    public void AreEqual_DiffersOnlyByCase_ReturnsFalse()
    {
        // The comparison is ordinal/byte-based; a case difference must not authenticate.
        ConstantTimeComparer.AreEqual("Token-Value", "token-value").Should().BeFalse();
    }

    [Fact]
    public void AreEqual_EqualMultiByteUnicodeTokens_ReturnsTrue()
    {
        // Ensures UTF-8 encoding is applied correctly for non-ASCII characters.
        ConstantTimeComparer.AreEqual("tökén-Ähre-ü", "tökén-Ähre-ü").Should().BeTrue();
    }

    [Fact]
    public void AreEqual_LongIdenticalTokens_ReturnsTrue()
    {
        // A token longer than a single SHA-256 input block must still compare equal.
        var token = new string('x', 500);
        ConstantTimeComparer.AreEqual(token, token).Should().BeTrue();
    }
}
