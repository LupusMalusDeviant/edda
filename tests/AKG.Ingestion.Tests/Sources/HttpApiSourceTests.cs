using Edda.AKG.Ingestion.Sources;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Sources;

/// <summary>Unit tests for the JSON mapping and URL building of <see cref="HttpApiSource"/>.</summary>
public sealed class HttpApiSourceTests
{
    [Fact]
    public void MapItems_NestedItemsPath_MapsFields()
    {
        const string json = """
        { "issues": [ { "key": "A-1", "fields": { "summary": "Hello", "description": "Body text" }, "self": "https://x/A-1" } ] }
        """;
        var options = new HttpApiSourceOptions
        {
            BaseUrl = "https://x",
            ListPath = "p",
            ItemsPath = "issues",
            IdField = "key",
            TitleField = "fields.summary",
            BodyField = "fields.description",
            UrlField = "self",
            SourceLabel = "jira",
        };

        var items = HttpApiSource.MapItems(json, options);

        items.Should().ContainSingle();
        items[0].Id.Should().Be("jira:A-1");
        items[0].Title.Should().Be("Hello");
        items[0].Body.Should().Be("Body text");
        items[0].SourceUrl.Should().Be("https://x/A-1");
        items[0].SourceKind.Should().Be("jira");
    }

    [Fact]
    public void MapItems_RootArray_WithDomain()
    {
        const string json = """[ { "id": "1", "title": "T", "content": "B" } ]""";
        var options = new HttpApiSourceOptions
        {
            BaseUrl = "https://x",
            ListPath = "p",
            IdField = "id",
            TitleField = "title",
            BodyField = "content",
            Domain = "ops",
        };

        var items = HttpApiSource.MapItems(json, options);

        items.Should().ContainSingle();
        items[0].Id.Should().Be("custom-http:1");
        items[0].Body.Should().Be("B");
        items[0].Domain.Should().Be("ops");
    }

    [Fact]
    public void MapItems_MissingId_FallsBackToIndex()
    {
        const string json = """[ { "name": "x" } ]""";
        var options = new HttpApiSourceOptions
        {
            BaseUrl = "https://x", ListPath = "p", IdField = "id", TitleField = "name",
        };

        var items = HttpApiSource.MapItems(json, options);

        items[0].Id.Should().Be("custom-http:0");
        items[0].Title.Should().Be("x");
    }

    [Fact]
    public void MapItems_UnresolvableItemsPath_ReturnsEmpty()
    {
        const string json = """{ "data": {} }""";
        var options = new HttpApiSourceOptions { BaseUrl = "https://x", ListPath = "p", ItemsPath = "missing" };

        HttpApiSource.MapItems(json, options).Should().BeEmpty();
    }

    [Fact]
    public void BuildUrl_OffsetMode_AppendsParams()
    {
        var options = new HttpApiSourceOptions
        {
            BaseUrl = "https://x/",
            ListPath = "/rest/search?jql=ORDER",
            PageMode = HttpApiPageMode.Offset,
            PageParam = "startAt",
            PageSizeParam = "maxResults",
            PageSize = 50,
        };

        HttpApiSource.BuildUrl(options, 100).Should().Be("https://x/rest/search?jql=ORDER&startAt=100&maxResults=50");
    }

    [Fact]
    public void BuildUrl_NoPagination_ReturnsPlainUrl()
    {
        var options = new HttpApiSourceOptions { BaseUrl = "https://x", ListPath = "items", PageMode = HttpApiPageMode.None };

        HttpApiSource.BuildUrl(options, 1).Should().Be("https://x/items");
    }

    [Fact]
    public void ParseOptions_FillsAuthTemplate_WithToken()
    {
        var config = new IngestionSourceConfig
        {
            Token = "abc",
            Settings = new Dictionary<string, string>
            {
                [HttpApiSource.BaseUrlKey] = "https://x",
                [HttpApiSource.ListPathKey] = "p",
                [HttpApiSource.AuthHeaderKey] = "Authorization",
                [HttpApiSource.AuthTemplateKey] = "Bearer {token}",
                [HttpApiSource.PageModeKey] = "page",
            },
        };

        var options = HttpApiSource.ParseOptions(config);

        options.Should().NotBeNull();
        options!.AuthHeader.Should().Be("Authorization");
        options.AuthValue.Should().Be("Bearer abc");
        options.PageMode.Should().Be(HttpApiPageMode.Page);
    }

    [Fact]
    public void ParseOptions_MissingRequired_ReturnsNull()
    {
        var config = new IngestionSourceConfig
        {
            Settings = new Dictionary<string, string> { [HttpApiSource.BaseUrlKey] = "https://x" },
        };

        HttpApiSource.ParseOptions(config).Should().BeNull();
    }
}
