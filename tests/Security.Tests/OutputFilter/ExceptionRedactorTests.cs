using Edda.Security.OutputFilter;

namespace Edda.Security.Tests.OutputFilter;

/// <summary>Unit tests for <see cref="ExceptionRedactor"/> using the real <see cref="SecretRedactor"/>.</summary>
public class ExceptionRedactorTests
{
    private readonly ISecretRedactor _redactor = new SecretRedactor();

    [Fact]
    public void RedactForLog_ExceptionWithApiKeyInMessage_RedactsTheKey()
    {
        var ex = new InvalidOperationException("Request failed for key sk-abcdefghijklmnopqrstuvwxyz0123");

        var text = ExceptionRedactor.RedactForLog(_redactor, ex);

        text.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz0123");
        text.Should().Contain("[API_KEY_SK]");
    }

    [Fact]
    public void RedactForLog_InnerExceptionWithSecret_RedactsIt()
    {
        var inner = new Exception("downstream token sk-zzzzzzzzzzzzzzzzzzzzzzzz9999");
        var ex = new InvalidOperationException("outer failure", inner);

        var text = ExceptionRedactor.RedactForLog(_redactor, ex);

        text.Should().NotContain("sk-zzzzzzzzzzzzzzzzzzzzzzzz9999");
    }

    [Fact]
    public void RedactForLog_NoSecret_KeepsTheOriginalMessage()
    {
        var ex = new InvalidOperationException("a normal failure with no secrets");

        var text = ExceptionRedactor.RedactForLog(_redactor, ex);

        text.Should().Contain("a normal failure with no secrets");
    }
}
