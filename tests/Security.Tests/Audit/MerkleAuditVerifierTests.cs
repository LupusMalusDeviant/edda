using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Security.Tests.Audit;

/// <summary>
/// Unit tests for <see cref="MerkleAuditVerifier"/>.
/// Covers valid-chain verification, deletion detection, reorder detection,
/// HMAC-tamper detection, and the empty-log edge case.
/// </summary>
public sealed class MerkleAuditVerifierTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates <paramref name="entryCount"/> valid signed audit entries using
    /// <see cref="HmacAuditLog"/> and returns the concatenated log content
    /// together with the raw key bytes written by the log.
    /// </summary>
    private static async Task<(string logContent, byte[] rawKey)> GenerateLogAsync(int entryCount)
    {
        byte[] rawKey = [];
        var    lines  = new List<string>();

        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(fs => fs.FileExists("data/.credential-key")).Returns(false);
        fsMock.Setup(fs => fs.FileExists(It.Is<string>(s => s != "data/.credential-key"))).Returns(false);
        fsMock
            .Setup(fs => fs.WriteAllBytesAsync(
                "data/.credential-key", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((_, bytes, _) => rawKey = bytes)
            .Returns(Task.CompletedTask);
        fsMock
            .Setup(fs => fs.AppendAllTextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, line, _) => lines.Add(line))
            .Returns(Task.CompletedTask);
        fsMock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));

        var auditLog = new HmacAuditLog(
            fsMock.Object, TimeProvider.System, NullLogger<HmacAuditLog>.Instance);

        for (var i = 0; i < entryCount; i++)
            await auditLog.LogAsync(AuditEvent.ModelCall, "user-test", $"Entry {i + 1}");

        return (string.Concat(lines), rawKey);
    }

    /// <summary>
    /// Creates a <see cref="MerkleAuditVerifier"/> whose filesystem mock serves
    /// <paramref name="rawKey"/> from the key file and <paramref name="logContent"/>
    /// from <c>data/audit.jsonl</c>.
    /// </summary>
    private static MerkleAuditVerifier CreateVerifier(byte[] rawKey, string logContent)
    {
        var fsMock = new Mock<IFileSystem>();

        fsMock.Setup(fs => fs.FileExists("data/.credential-key")).Returns(true);
        fsMock
            .Setup(fs => fs.ReadAllBytesAsync("data/.credential-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawKey);
        fsMock.Setup(fs => fs.FileExists("data/audit.jsonl")).Returns(true);
        fsMock
            .Setup(fs => fs.ReadAllTextAsync("data/audit.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(logContent);

        return new MerkleAuditVerifier(fsMock.Object, NullLogger<MerkleAuditVerifier>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_ValidChain_ReturnsIsValidTrue()
    {
        var (logContent, rawKey) = await GenerateLogAsync(3);
        var verifier             = CreateVerifier(rawKey, logContent);

        var result = await verifier.VerifyAsync("data/audit.jsonl");

        result.IsValid.Should().BeTrue();
        result.TotalEntries.Should().Be(3);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_DeletedEntry_DetectsChainBreak()
    {
        var (logContent, rawKey) = await GenerateLogAsync(3);

        // Remove the middle entry (index 1) — simulates a deleted log line.
        var allLines      = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tampered      = string.Join('\n', allLines.Where((_, i) => i != 1)) + '\n';
        var verifier      = CreateVerifier(rawKey, tampered);

        var result = await verifier.VerifyAsync("data/audit.jsonl");

        result.IsValid.Should().BeFalse();
        // Removing entry 2 causes both a sequence gap (3 follows 1) and a broken hash link.
        result.Issues.Should().Contain(i => i.IssueType == "SEQ_GAP" || i.IssueType == "CHAIN_BROKEN");
    }

    [Fact]
    public async Task VerifyAsync_ReorderedEntries_DetectsSeqGap()
    {
        var (logContent, rawKey) = await GenerateLogAsync(3);

        // Swap the first two entries — seq numbers will be out of order.
        var allLines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        (allLines[0], allLines[1]) = (allLines[1], allLines[0]);
        var reordered = string.Join('\n', allLines) + '\n';
        var verifier  = CreateVerifier(rawKey, reordered);

        var result = await verifier.VerifyAsync("data/audit.jsonl");

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.IssueType == "SEQ_GAP");
    }

    [Fact]
    public async Task VerifyAsync_TamperedEntry_DetectsHmacMismatch()
    {
        var (logContent, rawKey) = await GenerateLogAsync(3);

        // Modify the Entry JSON of the first line but keep the original MAC.
        // The stored MAC will no longer match the tampered content.
        var allLines    = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToArray();
        var firstSigned = JsonSerializer.Deserialize<SignedAuditEntry>(allLines[0])!;
        var tamperedEntry  = firstSigned.Entry.Replace("user-test", "user-hacked");
        var tamperedSigned = new SignedAuditEntry(tamperedEntry, firstSigned.Mac, firstSigned.EntryHash);
        allLines[0] = JsonSerializer.Serialize(tamperedSigned);
        var tampered = string.Join('\n', allLines) + '\n';
        var verifier = CreateVerifier(rawKey, tampered);

        var result = await verifier.VerifyAsync("data/audit.jsonl");

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.IssueType == "HMAC_INVALID");
    }

    [Fact]
    public async Task VerifyAsync_EmptyLog_ReturnsIsValidTrue()
    {
        var fsMock = new Mock<IFileSystem>();
        // Log file does not exist — verifier should treat it as an empty, valid chain.
        fsMock.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);

        var verifier = new MerkleAuditVerifier(fsMock.Object, NullLogger<MerkleAuditVerifier>.Instance);

        var result = await verifier.VerifyAsync("data/audit.jsonl");

        result.IsValid.Should().BeTrue();
        result.TotalEntries.Should().Be(0);
        result.Issues.Should().BeEmpty();
    }
}
