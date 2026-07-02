using Edda.Web.Services;

namespace Edda.Web.Tests;

/// <summary>
/// Unit tests for <see cref="MarkdigMarkdownRenderer"/>: XSS sanitisation (raw HTML escaped, dangerous
/// URL schemes neutralised) and that ordinary Markdown formatting is rendered.
/// </summary>
public sealed class MarkdigMarkdownRendererTests
{
    private static readonly MarkdigMarkdownRenderer Sut = new();

    [Fact]
    public void RenderToHtml_ScriptTag_IsEscapedNotEmittedAsMarkup()
    {
        var html = Sut.RenderToHtml("Hello <script>alert('xss')</script> world").Value;

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void RenderToHtml_RawImgWithOnerror_IsEscaped()
    {
        var html = Sut.RenderToHtml("<img src=x onerror=alert(1)>").Value;

        html.Should().NotContain("<img");
        html.Should().Contain("&lt;img");
    }

    [Fact]
    public void RenderToHtml_JavascriptLink_SchemeNeutralised()
    {
        var html = Sut.RenderToHtml("[click me](javascript:alert(1))").Value;

        html.Should().NotContain("javascript:");
        html.Should().Contain("click me");   // link text still rendered
    }

    [Fact]
    public void RenderToHtml_DataUriImage_SchemeNeutralised()
    {
        var html = Sut.RenderToHtml("![x](data:text/html;base64,PHNjcmlwdD4=)").Value;

        html.Should().NotContain("data:text/html");
    }

    [Fact]
    public void RenderToHtml_Bold_RendersStrong()
    {
        var html = Sut.RenderToHtml("This is **bold** text").Value;

        html.Should().Contain("<strong>bold</strong>");
    }

    [Fact]
    public void RenderToHtml_Heading_RendersHeadingTag()
    {
        var html = Sut.RenderToHtml("# Title").Value;

        html.Should().Contain("<h1").And.Contain("Title");
    }

    [Fact]
    public void RenderToHtml_CodeFence_RendersCodeBlock()
    {
        var html = Sut.RenderToHtml("```csharp\nvar x = 1;\n```").Value;

        html.Should().Contain("<code").And.Contain("var x = 1;");
    }

    [Fact]
    public void RenderToHtml_NullOrBlank_ReturnsEmptyMarkup()
    {
        Sut.RenderToHtml(null).Value.Should().BeEmpty();
        Sut.RenderToHtml("   ").Value.Should().BeEmpty();
    }
}
