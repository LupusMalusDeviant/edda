using Edda.Agent.Tdk;

namespace Edda.Agent.Tests.Tdk;

/// <summary>
/// Unit tests for <see cref="TdkLanguageMatcher"/>: language-targeting for TDK validator rules (F9).
/// </summary>
public class TdkLanguageMatcherTests
{
    [Theory]
    [InlineData("python")]
    [InlineData("csharp")]
    [InlineData("")]
    public void Applies_EmptyAppliesTo_ReturnsTrueForAnyLanguage(string blockLang)
        => TdkLanguageMatcher.Applies([], blockLang).Should().BeTrue();

    [Fact]
    public void Applies_NullAppliesTo_ReturnsTrue()
        => TdkLanguageMatcher.Applies(null, "python").Should().BeTrue();

    [Fact]
    public void Applies_MatchingLanguage_ReturnsTrue()
        => TdkLanguageMatcher.Applies(["python", "csharp"], "csharp").Should().BeTrue();

    [Fact]
    public void Applies_NonMatchingLanguage_ReturnsFalse()
        => TdkLanguageMatcher.Applies(["python"], "csharp").Should().BeFalse();

    [Theory]
    [InlineData("py", "python")]
    [InlineData("python", "py")]
    [InlineData("cs", "csharp")]
    [InlineData("c#", "csharp")]
    [InlineData("csharp", "cs")]
    [InlineData("js", "javascript")]
    [InlineData("ts", "typescript")]
    [InlineData("sh", "bash")]
    public void Applies_Aliases_MatchAcrossForms(string target, string blockLang)
        => TdkLanguageMatcher.Applies([target], blockLang).Should().BeTrue();

    [Fact]
    public void Applies_IsCaseInsensitive()
        => TdkLanguageMatcher.Applies(["Python"], "PYTHON").Should().BeTrue();

    [Fact]
    public void Applies_UnlabelledBlock_ReturnsTrue()
        => TdkLanguageMatcher.Applies(["python"], "").Should().BeTrue();

    [Fact]
    public void Canonicalize_ResolvesAliasesAndTrimsCase()
    {
        TdkLanguageMatcher.Canonicalize("py").Should().Be("python");
        TdkLanguageMatcher.Canonicalize("C#").Should().Be("csharp");
        TdkLanguageMatcher.Canonicalize("  JS ").Should().Be("javascript");
        TdkLanguageMatcher.Canonicalize(null).Should().Be("");
        TdkLanguageMatcher.Canonicalize("rust").Should().Be("rust");
    }
}
