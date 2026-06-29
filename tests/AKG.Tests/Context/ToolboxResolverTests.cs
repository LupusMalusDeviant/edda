using Edda.AKG.Context;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Context;

public class ToolboxResolverTests
{
    private readonly ToolboxResolver _resolver = new();

    [Theory]
    [InlineData("navigate to the login page", "tools.browser")]
    [InlineData("click on the submit button", "tools.browser")]
    [InlineData("take a screenshot of the page", "tools.browser")]
    public void Resolve_BrowserKeywords_ReturnsBrowserToolbox(string task, string expectedDomain)
    {
        var context = new TaskContext { Task = task };

        var result = _resolver.Resolve(context);

        result.Should().Contain(expectedDomain);
    }

    [Theory]
    [InlineData("search the web for latest news", "tools.web")]
    [InlineData("fetch data from the API", "tools.web")]
    [InlineData("make an HTTP request to the endpoint", "tools.web")]
    public void Resolve_WebKeywords_ReturnsWebToolbox(string task, string expectedDomain)
    {
        var context = new TaskContext { Task = task };

        var result = _resolver.Resolve(context);

        result.Should().Contain(expectedDomain);
    }

    [Theory]
    [InlineData("run a python script to process data", "tools.code")]
    [InlineData("execute shell command to list files", "tools.code")]
    public void Resolve_CodeKeywords_ReturnsCodeToolbox(string task, string expectedDomain)
    {
        var context = new TaskContext { Task = task };

        var result = _resolver.Resolve(context);

        result.Should().Contain(expectedDomain);
    }

    [Fact]
    public void Resolve_MultipleToolboxes_ReturnsAll()
    {
        var context = new TaskContext
        {
            Task = "search the web and run a python script",
        };

        var result = _resolver.Resolve(context);

        result.Should().Contain("tools.web");
        result.Should().Contain("tools.code");
    }

    [Fact]
    public void Resolve_AlwaysIncludesCustomTools()
    {
        var context = new TaskContext { Task = "search the web" };

        var result = _resolver.Resolve(context);

        result.Should().Contain("custom-tools");
    }

    [Fact]
    public void Resolve_NoToolKeywords_ReturnsAllToolboxes_GracefulDegradation()
    {
        var context = new TaskContext { Task = "hello, how are you?" };

        var result = _resolver.Resolve(context);

        // Fallback: all toolboxes loaded when no keywords match
        result.Should().Contain("custom-tools");
        result.Count.Should().BeGreaterThan(1);
        foreach (var domain in ToolboxResolver.ToolboxKeywords.Keys)
        {
            result.Should().Contain(domain);
        }
    }

    [Fact]
    public void Resolve_ConceptsAlsoMatchToolboxes()
    {
        var context = new TaskContext
        {
            Task = "hilf mir",
            Concepts = ["docker", "container"],
        };

        var result = _resolver.Resolve(context);

        result.Should().Contain("tools.devops");
    }

    [Theory]
    [InlineData("merk dir meinen Namen", "tools.memory")]
    [InlineData("manage my credentials", "tools.security")]
    [InlineData("set up a trigger for daily checks", "tools.scheduling")]
    [InlineData("manage files in the project", "tools.files")]
    [InlineData("check the knowledge graph", "tools.knowledge")]
    [InlineData("start an autonomous research task", "tools.multiagent")]
    public void Resolve_VariousCategories_ReturnsCorrectToolbox(string task, string expectedDomain)
    {
        var context = new TaskContext { Task = task };

        var result = _resolver.Resolve(context);

        result.Should().Contain(expectedDomain);
    }
}
