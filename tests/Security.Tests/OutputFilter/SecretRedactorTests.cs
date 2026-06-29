using Edda.Security.OutputFilter;

namespace Edda.Security.Tests.OutputFilter;

public sealed class SecretRedactorTests
{
    private readonly SecretRedactor _sut = new();

    [Fact]
    public void Redact_AnthropicApiKey_ReplacedWithPlaceholder()
    {
        var input = "Use this key: sk-ant-api03-abcdefghij1234567890ABCDEF to call Anthropic.";

        var result = _sut.Redact(input);

        result.Should().Contain("[API_KEY_ANT]");
        result.Should().NotContain("sk-ant-api03-abcdefghij1234567890ABCDEF");
    }

    [Fact]
    public void Redact_OpenAiStyleSkKey_ReplacedWithPlaceholder()
    {
        var input = "My key is sk-abcdefghijklmnopqrstu12345 for the API.";

        var result = _sut.Redact(input);

        result.Should().Contain("[API_KEY_SK]");
        result.Should().NotContain("sk-abcdefghijklmnopqrstu12345");
    }

    [Fact]
    public void Redact_BearerToken_TokenPortionReplaced()
    {
        var input = "Authorization: Bearer abc123TokenValueHere";

        var result = _sut.Redact(input);

        result.Should().Contain("Bearer [TOKEN]");
        result.Should().NotContain("abc123TokenValueHere");
    }

    [Fact]
    public void Redact_AwsAccessKey_ReplacedWithPlaceholder()
    {
        var input = "AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE in environment.";

        var result = _sut.Redact(input);

        result.Should().Contain("[AWS_KEY]");
        result.Should().NotContain("AKIAIOSFODNN7EXAMPLE");
    }

    [Fact]
    public void Redact_GitHubPersonalAccessToken_ReplacedWithPlaceholder()
    {
        // ghp_ followed by exactly 36 alphanumeric chars
        var input = "token ghp_abcdefghijklmnopqrstuvwxyz1234567890 was used";

        var result = _sut.Redact(input);

        result.Should().Contain("[GITHUB_TOKEN]");
        result.Should().NotContain("ghp_abcdefghijklmnopqrstuvwxyz1234567890");
    }

    [Fact]
    public void Redact_PasswordAssignment_ValueRedacted()
    {
        var input = "Config: password=SuperSecret123!";

        var result = _sut.Redact(input);

        result.Should().Contain("password=[REDACTED]");
        result.Should().NotContain("SuperSecret123!");
    }

    [Fact]
    public void Redact_PasswordAssignmentCaseInsensitive_ValueRedacted()
    {
        var input = "PASSWORD=topsecret";

        var result = _sut.Redact(input);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("topsecret");
    }

    [Fact]
    public void Redact_NormalText_ReturnedUnchanged()
    {
        var input = "The weather today is sunny with a high of 22 degrees.";

        var result = _sut.Redact(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Redact_EmptyString_ReturnedUnchanged()
    {
        var result = _sut.Redact(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Redact_MultipleSecrets_AllReplaced()
    {
        var input = "key=sk-ant-api03-ABCDEFGHIJKLMNOPQRSTU1234567890 and token=Bearer xyz999";

        var result = _sut.Redact(input);

        result.Should().Contain("[API_KEY_ANT]");
        result.Should().Contain("Bearer [TOKEN]");
        result.Should().NotContain("sk-ant-api03-ABCDEFGHIJKLMNOPQRSTU1234567890");
        result.Should().NotContain("xyz999");
    }

    [Fact]
    public void Redact_PrivateKeyBlock_ReplacedWithPlaceholder()
    {
        var input = "Key:\n-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----\nDone.";

        var result = _sut.Redact(input);

        result.Should().Contain("[PRIVATE_KEY]");
        result.Should().NotContain("MIIEowIBAAKCAQEA");
    }

    [Fact]
    public void Redact_AnthropicKeyTakesPriorityOverGenericSkPattern()
    {
        // An sk-ant- key must be caught by the ANT pattern, not the generic SK pattern
        var input = "sk-ant-api03-longkeyvalueforthispatternmatch1234";

        var result = _sut.Redact(input);

        result.Should().Contain("[API_KEY_ANT]");
        result.Should().NotContain("[API_KEY_SK]");
    }
}
