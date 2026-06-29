using Edda.AKG.Graph;
using Edda.AKG.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edda.AKG.Tests.Graph;

public sealed class WorldKnowledgeSeederTests
{
    private const string WorldDir = "knowledge/world";

    private static string MakeValidFrontmatter(string id = "world-test") =>
        $"""
        ---
        id: {id}
        title: Test Knowledge
        domain: world
        type: WorldKnowledge
        priority: Low
        tags: [testing, knowledge]
        author: system
        ---

        ## Test Knowledge

        This is a test world knowledge entry.
        """;

    [Fact]
    public async Task CountAsync_WhenNoNodes_ReturnsZero()
    {
        var cypher = new FakeCypherExecutor();
        cypher.DefaultResult = [new Dictionary<string, object?> { ["n"] = 0L }];
        var sut = CreateSut(cypher);

        var count = await sut.CountAsync();

        count.Should().Be(0L);
        cypher.ExecutedQueries.Should().ContainSingle(q => q.Contains("WorldKnowledge") && q.Contains("count"));
    }

    [Fact]
    public async Task CountAsync_WhenNodesExist_ReturnsCount()
    {
        var cypher = new FakeCypherExecutor();
        cypher.DefaultResult = [new Dictionary<string, object?> { ["n"] = 5L }];
        var sut = CreateSut(cypher);

        var count = await sut.CountAsync();

        count.Should().Be(5L);
    }

    [Fact]
    public async Task SeedFromDirectoryAsync_EmptyDirectory_ReturnsZero()
    {
        var fs = new InMemoryFileSystem();
        fs.EnsureDirectoryExists(WorldDir);
        var cypher = new FakeCypherExecutor();
        var sut = CreateSut(cypher, fs);

        var result = await sut.SeedFromDirectoryAsync(WorldDir);

        result.Should().Be(0);
        cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task SeedFromDirectoryAsync_DirectoryDoesNotExist_ReturnsZero()
    {
        var fs = new InMemoryFileSystem(); // directory not added
        var cypher = new FakeCypherExecutor();
        var sut = CreateSut(cypher, fs);

        var result = await sut.SeedFromDirectoryAsync(WorldDir);

        result.Should().Be(0);
        cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task SeedFromDirectoryAsync_ValidFile_UpsertsMergeNode()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($"{WorldDir}/oop.md", MakeValidFrontmatter("world-oop"));
        var cypher = new FakeCypherExecutor();
        var sut = CreateSut(cypher, fs);

        var result = await sut.SeedFromDirectoryAsync(WorldDir);

        result.Should().Be(1);
        cypher.ExecutedWriteQueries.Should().ContainSingle(q =>
            q.Contains("MERGE") && q.Contains("WorldKnowledge"));
    }

    [Fact]
    public async Task SeedFromDirectoryAsync_InvalidFile_SkipsAndContinues()
    {
        var fs = new InMemoryFileSystem();
        // Invalid: no id field
        fs.AddFile($"{WorldDir}/invalid.md", "---\ntitle: No id here\n---\n\nBody.");
        // Valid file
        fs.AddFile($"{WorldDir}/valid.md", MakeValidFrontmatter("world-valid"));
        var cypher = new FakeCypherExecutor();
        var sut = CreateSut(cypher, fs);

        var result = await sut.SeedFromDirectoryAsync(WorldDir);

        // Only the valid file succeeds
        result.Should().Be(1);
    }

    [Fact]
    public async Task SeedIfEmptyAsync_CountZero_SeedsFromDirectory()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($"{WorldDir}/test.md", MakeValidFrontmatter("world-empty-test"));
        var cypher = new FakeCypherExecutor();
        // Count returns 0
        cypher.DefaultResult = [new Dictionary<string, object?> { ["n"] = 0L }];
        var sut = CreateSut(cypher, fs);

        var result = await sut.SeedIfEmptyAsync(WorldDir);

        result.Should().Be(1);
        cypher.ExecutedWriteQueries.Should().ContainSingle(q => q.Contains("MERGE"));
    }

    [Fact]
    public async Task SeedIfEmptyAsync_CountPositive_Skips()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($"{WorldDir}/test.md", MakeValidFrontmatter("world-skip-test"));
        var cypher = new FakeCypherExecutor();
        // Count returns 3 (already seeded)
        cypher.DefaultResult = [new Dictionary<string, object?> { ["n"] = 3L }];
        var sut = CreateSut(cypher, fs);

        var result = await sut.SeedIfEmptyAsync(WorldDir);

        result.Should().Be(0);
        cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReloadAsync_DeletesThenSeeds()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile($"{WorldDir}/reload.md", MakeValidFrontmatter("world-reload"));
        var cypher = new FakeCypherExecutor();
        var sut = CreateSut(cypher, fs);

        var result = await sut.ReloadAsync(WorldDir);

        result.Should().Be(1);
        // First write query must be the DELETE, second the MERGE
        cypher.ExecutedWriteQueries.Should().HaveCount(2);
        cypher.ExecutedWriteQueries[0].Should().Contain("DETACH DELETE");
        cypher.ExecutedWriteQueries[1].Should().Contain("MERGE");
    }

    private static WorldKnowledgeSeeder CreateSut(FakeCypherExecutor cypher, InMemoryFileSystem? fs = null)
        => new(fs ?? new InMemoryFileSystem(), cypher, NullLogger<WorldKnowledgeSeeder>.Instance);
}
