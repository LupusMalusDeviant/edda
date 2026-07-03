using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Graph;

/// <summary>E10: <see cref="RuleRecycleBin"/> over the in-memory executor (real query shapes).</summary>
public sealed class RuleRecycleBinTests
{
    private readonly ICypherExecutor _executor = new InMemoryCypherExecutor();
    private readonly Mock<IAuditLog> _auditLog = new();
    private readonly RuleRecycleBin _sut;

    public RuleRecycleBinTests()
    {
        _sut = new RuleRecycleBin(_executor, _auditLog.Object, NullLogger<RuleRecycleBin>.Instance);
    }

    private const string Now = "2026-07-03T12:00:00.0000000+00:00";

    private Task Upsert(string id, string? ownerId) => _executor.ExecuteAsync(
        """
        MERGE (r:Rule {id: $id})
        SET r.type = $type,
            r.domain = $domain,
            r.priority = $priority,
            r.body = $body,
            r.tags = $tags,
            r.ownerId = $ownerId,
            r.implies = $implies,
            r.conflictsWith = $conflictsWith,
            r.exceptionFor = $exceptionFor,
            r.requires = $requires,
            r.supersedes = $supersedes,
            r.related = $related,
            r.chunkStyle = $chunkStyle,
            r.validFrom = coalesce(r.validFrom, $now)
        """,
        new
        {
            id,
            type = "Rule",
            domain = "testing",
            priority = "Medium",
            body = $"Body of {id}.",
            tags = Array.Empty<string>(),
            ownerId,
            implies = Array.Empty<string>(),
            conflictsWith = Array.Empty<string>(),
            exceptionFor = Array.Empty<string>(),
            requires = Array.Empty<string>(),
            supersedes = Array.Empty<string>(),
            related = Array.Empty<string>(),
            chunkStyle = (string?)null,
            now = Now,
        });

    private Task SoftDelete(string id, string userId) => _executor.ExecuteAsync(
        """
        MATCH (r:Rule {id: $ruleId})
        SET r.deletedAt = $now,
            r.deletedBy = $userId,
            r.validUntil = coalesce(r.validUntil, $now)
        """,
        new { ruleId = id, userId, now = Now });

    [Fact]
    public async Task ListAsync_NonAdmin_SeesOnlyOwnDeletedRules()
    {
        await Upsert("mine", ownerId: "u1");
        await Upsert("foreign", ownerId: "u2");
        await SoftDelete("mine", "u1");
        await SoftDelete("foreign", "u2");

        var list = await _sut.ListAsync("u1");

        list.Should().ContainSingle().Which.Id.Should().Be("mine");
        list[0].DeletedBy.Should().Be("u1");
        list[0].BodyPreview.Should().Contain("Body of mine");
    }

    [Fact]
    public async Task ListAsync_Admin_SeesAllDeletedRules()
    {
        await Upsert("mine", ownerId: "u1");
        await Upsert("foreign", ownerId: "u2");
        await SoftDelete("mine", "u1");
        await SoftDelete("foreign", "u2");

        var list = await _sut.ListAsync("u1", isAdmin: true);

        list.Select(i => i.Id).Should().BeEquivalentTo(["mine", "foreign"]);
    }

    [Fact]
    public async Task RestoreAsync_DeletedOwnRule_ReturnsTrue_AndAuditsRuleRestored()
    {
        await Upsert("mine", ownerId: "u1");
        await SoftDelete("mine", "u1");

        var restored = await _sut.RestoreAsync("mine", "u1");

        restored.Should().BeTrue();
        (await _sut.ListAsync("u1")).Should().BeEmpty("the rule left the bin");
        _auditLog.Verify(a => a.LogAsync(
            AuditEvent.RuleRestored, "u1", It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_ForeignRule_ThrowsUnauthorized()
    {
        await Upsert("foreign", ownerId: "u2");
        await SoftDelete("foreign", "u2");

        var act = async () => await _sut.RestoreAsync("foreign", "u1");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RestoreAsync_NotDeleted_ReturnsFalse()
    {
        await Upsert("active", ownerId: "u1");

        var restored = await _sut.RestoreAsync("active", "u1");

        restored.Should().BeFalse();
        _auditLog.Verify(a => a.LogAsync(
            It.IsAny<AuditEvent>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PurgeAsync_DeletedRule_RemovesPermanently_AndAuditsRulePurged()
    {
        await Upsert("mine", ownerId: "u1");
        await SoftDelete("mine", "u1");

        var purged = await _sut.PurgeAsync("mine", "u1");

        purged.Should().BeTrue();
        (await _sut.ListAsync("u1")).Should().BeEmpty();
        (await _sut.RestoreAsync("mine", "u1")).Should().BeFalse("the rule is gone for good");
        _auditLog.Verify(a => a.LogAsync(
            AuditEvent.RulePurged, "u1", It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
