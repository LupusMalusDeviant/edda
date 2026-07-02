using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>Unit tests for <see cref="ValidatorScriptHash"/> (F7 validator versioning).</summary>
public sealed class ValidatorScriptHashTests
{
    [Fact]
    public void Compute_SameScript_ReturnsSameHash()
        => ValidatorScriptHash.Compute("print('x')").Should().Be(ValidatorScriptHash.Compute("print('x')"));

    [Fact]
    public void Compute_DifferentScripts_ReturnDifferentHashes()
        => ValidatorScriptHash.Compute("a").Should().NotBe(ValidatorScriptHash.Compute("b"));

    [Fact]
    public void Compute_ReturnsLowercaseHex_Of64Chars()
        => ValidatorScriptHash.Compute("x").Should().MatchRegex("^[0-9a-f]{64}$");

    [Fact]
    public void Compute_NullOrEmpty_ReturnsNull()
    {
        ValidatorScriptHash.Compute(null).Should().BeNull();
        ValidatorScriptHash.Compute(string.Empty).Should().BeNull();
    }
}
