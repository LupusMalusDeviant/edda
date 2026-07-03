using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Security.Tests.Audit;

/// <summary>E10: <see cref="HmacAuditLog.ReadRecentAsync"/> — the read API behind the UI history view.</summary>
public sealed class HmacAuditLogReadTests
{
    /// <summary>
    /// SUT whose appended lines are captured and served back as the (single) daily audit file, so
    /// reads observe exactly what was written.
    /// </summary>
    private static (HmacAuditLog Sut, Mock<IFileSystem> Fs, List<string> Lines) CreateSut()
    {
        var fs = new Mock<IFileSystem>();
        var lines = new List<string>();

        fs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        fs.Setup(f => f.DirectoryExists("data/audit.jsonl")).Returns(true);
        fs.Setup(f => f.EnumerateFiles("data/audit.jsonl", "audit-*.jsonl", It.IsAny<bool>()))
            .Returns(() => lines.Count == 0
                ? []
                : new[] { "data/audit.jsonl/audit-2026-07-03.jsonl" });
        fs.Setup(f => f.CombinePath(It.IsAny<string[]>()))
            .Returns<string[]>(parts => string.Join("/", parts));
        fs.Setup(f => f.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fs.Setup(f => f.AppendAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => lines.Add(content))
            .Returns(Task.CompletedTask);
        fs.Setup(f => f.EnsureDirectoryExists(It.IsAny<string>()));
        fs.Setup(f => f.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => string.Concat(lines));

        var sut = new HmacAuditLog(fs.Object, TimeProvider.System, NullLogger<HmacAuditLog>.Instance);
        return (sut, fs, lines);
    }

    [Fact]
    public async Task ReadRecentAsync_AfterWrites_ReturnsNewestFirstWithValidHmac()
    {
        var (sut, _, _) = CreateSut();
        await sut.LogAsync(AuditEvent.RuleProposed, "u1", "first");
        await sut.LogAsync(AuditEvent.RuleDeleted, "u1", "second");
        await sut.LogAsync(AuditEvent.RuleRestored, "u1", "third");

        var entries = await sut.ReadRecentAsync(2);

        entries.Should().HaveCount(2);
        entries[0].Description.Should().Be("third");
        entries[0].Seq.Should().Be(3);
        entries[1].Description.Should().Be("second");
        entries.Should().OnlyContain(e => e.Valid, "untampered entries verify against the HMAC key");
    }

    [Fact]
    public async Task ReadRecentAsync_TamperedEntry_MarkedInvalid()
    {
        var (sut, _, lines) = CreateSut();
        await sut.LogAsync(AuditEvent.RuleProposed, "u1", "original-text");

        lines[0] = lines[0].Replace("original-text", "tampered-text");

        var entries = await sut.ReadRecentAsync(10);

        entries.Should().ContainSingle();
        entries[0].Description.Should().Be("tampered-text");
        entries[0].Valid.Should().BeFalse("the HMAC no longer matches the modified entry");
    }

    [Fact]
    public async Task ReadRecentAsync_NoDirectory_ReturnsEmpty()
    {
        var (sut, fs, _) = CreateSut();
        fs.Setup(f => f.DirectoryExists("data/audit.jsonl")).Returns(false);

        var entries = await sut.ReadRecentAsync(10);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadRecentAsync_LimitZero_ReturnsEmpty()
    {
        var (sut, _, _) = CreateSut();
        await sut.LogAsync(AuditEvent.RuleProposed, "u1", "entry");

        var entries = await sut.ReadRecentAsync(0);

        entries.Should().BeEmpty();
    }
}
