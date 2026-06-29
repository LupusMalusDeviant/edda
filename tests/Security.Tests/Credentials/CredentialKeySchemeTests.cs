using Edda.Security.Credentials;

namespace Edda.Security.Tests.Credentials;

public sealed class CredentialKeySchemeTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("llm:anthropic")]
    [InlineData("embed.index")]
    [InlineData("a-b_c.d:e")]
    public void IsValidName_ValidName_ReturnsTrue(string name)
        => CredentialKeyScheme.IsValidName(name).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData("has space")]
    [InlineData("bad/slash")]
    [InlineData("bang!")]
    public void IsValidName_InvalidName_ReturnsFalse(string? name)
        => CredentialKeyScheme.IsValidName(name).Should().BeFalse();

    [Fact]
    public void IsValidName_TooLong_ReturnsFalse()
    {
        var name = new string('a', CredentialKeyScheme.MaxNameLength + 1);

        CredentialKeyScheme.IsValidName(name).Should().BeFalse();
    }

    [Fact]
    public void Scope_ValidInput_ReturnsUserScopedKey()
        => CredentialKeyScheme.Scope("local", "llm:anthropic").Should().Be("local:llm:anthropic");

    [Fact]
    public void Scope_EmptyUserId_Throws()
    {
        var act = () => CredentialKeyScheme.Scope("", "openai");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Scope_InvalidName_Throws()
    {
        var act = () => CredentialKeyScheme.Scope("local", "bad name");

        act.Should().Throw<ArgumentException>();
    }
}
