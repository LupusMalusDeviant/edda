using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Security.Tests.Audit;

/// <summary>
/// Unit tests for <see cref="HmacAuditLog"/>.
/// Verifies log persistence, HMAC signing, Merkle-chain construction,
/// chain-state restoration, and declassification/verification helpers.
/// </summary>
public sealed class HmacAuditLogTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh SUT with no pre-existing log directory or key file.
    /// The generated raw HMAC key bytes are captured via the <c>WriteAllBytesAsync</c> mock
    /// and stored in <paramref name="rawKeyRef"/> after the first <see cref="HmacAuditLog.LogAsync"/> call.
    /// <para>
    /// The mock is configured with:
    /// <list type="bullet">
    ///   <item>No existing key file — a new one is generated on first write.</item>
    ///   <item><c>DirectoryExists("data/audit.jsonl")</c> → <c>false</c> — no prior audit directory.</item>
    ///   <item><c>CombinePath</c> → joins parts with <c>/</c>.</item>
    ///   <item><c>AppendAllTextAsync</c> → lines are captured in the returned list.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static (HmacAuditLog sut, Mock<IFileSystem> fsMock, List<string> captured)
        CreateSut(out byte[][] rawKeyRef)
    {
        var fsMock   = new Mock<IFileSystem>();
        var captured = new List<string>();
        var keyStore = new byte[1][];          // box so the callback can update via index

        fsMock.Setup(fs => fs.FileExists("data/.credential-key")).Returns(false);
        fsMock.Setup(fs => fs.FileExists(It.Is<string>(s => s != "data/.credential-key"))).Returns(false);

        // No existing audit directory → InitializeChainStateAsync is a no-op on fresh starts.
        fsMock.Setup(fs => fs.DirectoryExists("data/audit.jsonl")).Returns(false);

        // EnumerateFiles would only be reached if DirectoryExists returns true; provide
        // an empty result as a safety net for any test that tweaks the DirectoryExists mock.
        fsMock
            .Setup(fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns([]);

        // CombinePath: join parts with "/" so GetDatedFilePath() returns a deterministic path.
        fsMock
            .Setup(fs => fs.CombinePath(It.IsAny<string[]>()))
            .Returns<string[]>(parts => string.Join("/", parts));

        fsMock
            .Setup(fs => fs.WriteAllBytesAsync(
                "data/.credential-key",
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((_, bytes, _) => keyStore[0] = bytes)
            .Returns(Task.CompletedTask);

        fsMock
            .Setup(fs => fs.AppendAllTextAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => captured.Add(content))
            .Returns(Task.CompletedTask);

        fsMock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));

        var sut = new HmacAuditLog(fsMock.Object, TimeProvider.System, NullLogger<HmacAuditLog>.Instance);
        rawKeyRef = keyStore;
        return (sut, fsMock, captured);
    }

    /// <summary>Convenience overload for tests that do not need the raw key.</summary>
    private static (HmacAuditLog sut, Mock<IFileSystem> fsMock, List<string> captured) CreateSut()
        => CreateSut(out _);

    /// <summary>
    /// Parses a captured log line into a <see cref="SignedAuditEntry"/> and its inner <see cref="AuditEntry"/>.
    /// </summary>
    private static (SignedAuditEntry signed, AuditEntry entry) Parse(string line)
    {
        var signed = JsonSerializer.Deserialize<SignedAuditEntry>(line.TrimEnd('\n'))!;
        var entry  = JsonSerializer.Deserialize<AuditEntry>(signed.Entry)!;
        return (signed, entry);
    }

    // ── Basic persistence ─────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_WritesEntryToAuditFile()
    {
        var (sut, fsMock, _) = CreateSut();

        await sut.LogAsync(AuditEvent.ModelCall, "user-42", "Test log entry");

        // The actual path is a dated file inside data/audit.jsonl/, e.g.
        // "data/audit.jsonl/audit-2026-03-05.jsonl". We verify the directory prefix.
        fsMock.Verify(
            fs => fs.AppendAllTextAsync(
                It.Is<string>(p => p != null && p.StartsWith("data/audit.jsonl/")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogAsync_EntryContainsEventTypeAndUserId()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.ToolExecute, "user-99", "Tool ran successfully");

        captured.Should().HaveCount(1);
        var line = captured[0];
        // EventType and UserId appear inside the Entry JSON embedded in the SignedAuditEntry line
        line.Should().Contain("ToolExecute");
        line.Should().Contain("user-99");
    }

    [Fact]
    public async Task LogAsync_EntryContainsHmacSignature()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.CredentialAccess, "user-1", "Credential read");

        captured.Should().HaveCount(1);
        var (signed, entry) = Parse(captured[0]);

        // Inner entry must contain the correct user
        entry.UserId.Should().Be("user-1");

        // MAC must be a non-empty Base64 string encoding 32 bytes (HMAC-SHA256 output)
        signed.Mac.Should().NotBeNullOrWhiteSpace();
        var macBytes = Convert.FromBase64String(signed.Mac);
        macBytes.Should().HaveCount(32);

        // EntryHash must be a 64-character lowercase hex string (SHA-256 output)
        signed.EntryHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task LogAsync_EntryContainsDescription()
    {
        var (sut, _, captured) = CreateSut();
        const string description = "Something happened here";

        await sut.LogAsync(AuditEvent.InputSanitized, "user-x", description);

        captured[0].Should().Contain(description);
    }

    [Fact]
    public async Task LogAsync_WithMetadata_MetadataSerializedInEntry()
    {
        var (sut, _, captured) = CreateSut();
        var metadata = new Dictionary<string, object?> { ["tool"] = "web_search", ["count"] = 3 };

        await sut.LogAsync(AuditEvent.ToolExecute, "user-m", "Tool called", metadata);

        captured[0].Should().Contain("web_search");
    }

    [Fact]
    public async Task LogAsync_ConcurrentWrites_AllSucceed()
    {
        var (sut, _, captured) = CreateSut();
        const int count = 10;

        var tasks = Enumerable.Range(0, count)
            .Select(i => sut.LogAsync(AuditEvent.ModelCall, $"user-{i}", $"Entry {i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        captured.Should().HaveCount(count);
    }

    [Fact]
    public async Task LogAsync_KeyFileExistsOnDisk_KeyIsReadFromFile()
    {
        var fsMock      = new Mock<IFileSystem>();
        var existingKey = new byte[32];
        new Random(42).NextBytes(existingKey);

        fsMock.Setup(fs => fs.FileExists("data/.credential-key")).Returns(true);
        fsMock
            .Setup(fs => fs.ReadAllBytesAsync("data/.credential-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingKey);

        // No existing audit directory — chain init is skipped.
        fsMock.Setup(fs => fs.DirectoryExists("data/audit.jsonl")).Returns(false);
        fsMock
            .Setup(fs => fs.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns([]);
        fsMock
            .Setup(fs => fs.CombinePath(It.IsAny<string[]>()))
            .Returns<string[]>(parts => string.Join("/", parts));
        fsMock
            .Setup(fs => fs.AppendAllTextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fsMock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));

        var sut = new HmacAuditLog(fsMock.Object, TimeProvider.System, NullLogger<HmacAuditLog>.Instance);

        await sut.LogAsync(AuditEvent.ConfigChanged, "admin", "Config updated");

        fsMock.Verify(
            fs => fs.WriteAllBytesAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not write a new key file when one already exists");
    }

    // ── VerifyEntry ───────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyEntry_ValidEntry_ReturnsTrue()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.RuleProposed, "user-v", "Rule added");

        var (signed, _) = Parse(captured[0]);
        sut.VerifyEntry(signed).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyEntry_TamperedJson_ReturnsFalse()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.RuleDeleted, "user-t", "Rule removed");

        var (signed, _) = Parse(captured[0]);

        // Tamper with the inner Entry JSON while keeping the original MAC
        var tamperedEntry  = signed.Entry.Replace("user-t", "user-hacker");
        var tamperedSigned = new SignedAuditEntry(tamperedEntry, signed.Mac, signed.EntryHash);

        sut.VerifyEntry(tamperedSigned).Should().BeFalse();
    }

    // ── Merkle chain ──────────────────────────────────────────────────────────

    [Fact]
    public async Task HmacAuditLog_FirstEntry_PrevHashIsNull()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.ModelCall, "user-a", "First");

        var (_, entry) = Parse(captured[0]);
        entry.Seq.Should().Be(1);
        entry.PrevHash.Should().BeNull();
    }

    [Fact]
    public async Task HmacAuditLog_SecondEntry_PrevHashMatchesFirst()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.ModelCall, "user-a", "First");
        await sut.LogAsync(AuditEvent.ModelCall, "user-a", "Second");

        var (firstSigned,  firstEntry)  = Parse(captured[0]);
        var (_,            secondEntry) = Parse(captured[1]);

        firstEntry.Seq.Should().Be(1);
        secondEntry.Seq.Should().Be(2);
        secondEntry.PrevHash.Should().Be(firstSigned.EntryHash);
    }

    [Fact]
    public async Task HmacAuditLog_AppendedEntry_IsVerifiable()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.ModelCall, "user-b", "Verifiable entry");

        var (signed, _) = Parse(captured[0]);
        sut.VerifyEntry(signed).Should().BeTrue();
    }

    [Fact]
    public async Task HmacAuditLog_TamperedEntry_HmacFails()
    {
        var (sut, _, captured) = CreateSut();

        await sut.LogAsync(AuditEvent.ModelCall, "user-c", "Tamper target");

        var (signed, _)    = Parse(captured[0]);
        var tamperedEntry  = signed.Entry.Replace("user-c", "attacker");
        var tamperedSigned = new SignedAuditEntry(tamperedEntry, signed.Mac, signed.EntryHash);

        sut.VerifyEntry(tamperedSigned).Should().BeFalse();
    }

    [Fact]
    public async Task HmacAuditLog_ConcurrentWrites_SequenceMonotonic()
    {
        var (sut, _, captured) = CreateSut();
        const int count = 20;

        await Task.WhenAll(Enumerable.Range(0, count)
            .Select(i => sut.LogAsync(AuditEvent.ModelCall, $"u{i}", $"Entry {i}")));

        captured.Should().HaveCount(count);

        var seqs = captured
            .Select(line => Parse(line).entry.Seq)
            .OrderBy(s => s)
            .ToList();

        seqs.Should().Equal(Enumerable.Range(1, count).Select(i => (long)i));
    }

    [Fact]
    public async Task HmacAuditLog_InitializeChainState_RestoresSeqAndHash()
    {
        // Use a fixed date so the dated file path is deterministic across test runs.
        var fixedTime  = new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
        var timeProv   = new FixedTimeProvider(fixedTime);
        var datedPath  = "data/audit.jsonl/audit-2026-03-05.jsonl";

        // ── Arrange: SUT1 — no existing log directory, generates and captures the raw HMAC key ──
        byte[] rawKey  = [];
        var captured1  = new List<string>();
        var fsMock1    = new Mock<IFileSystem>();

        fsMock1.Setup(fs => fs.FileExists("data/.credential-key")).Returns(false);
        fsMock1.Setup(fs => fs.DirectoryExists("data/audit.jsonl")).Returns(false);
        fsMock1
            .Setup(fs => fs.WriteAllBytesAsync(
                "data/.credential-key", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((_, bytes, _) => rawKey = bytes)
            .Returns(Task.CompletedTask);
        fsMock1
            .Setup(fs => fs.AppendAllTextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, line, _) => captured1.Add(line))
            .Returns(Task.CompletedTask);
        fsMock1
            .Setup(fs => fs.CombinePath(It.IsAny<string[]>()))
            .Returns<string[]>(parts => string.Join("/", parts));
        fsMock1.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));

        var sut1 = new HmacAuditLog(fsMock1.Object, timeProv, NullLogger<HmacAuditLog>.Instance);
        await sut1.LogAsync(AuditEvent.ModelCall, "user-init", "First entry");

        var firstLine   = captured1[0];
        var firstSigned = JsonSerializer.Deserialize<SignedAuditEntry>(firstLine.TrimEnd('\n'))!;

        // ── Arrange: SUT2 — same key + existing audit directory = simulates a process restart ──
        var captured2 = new List<string>();
        var fsMock2   = new Mock<IFileSystem>();

        fsMock2.Setup(fs => fs.FileExists("data/.credential-key")).Returns(true);
        fsMock2
            .Setup(fs => fs.ReadAllBytesAsync("data/.credential-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawKey);

        // Audit directory exists and contains the dated file from the first SUT's writes.
        fsMock2.Setup(fs => fs.DirectoryExists("data/audit.jsonl")).Returns(true);
        fsMock2
            .Setup(fs => fs.EnumerateFiles("data/audit.jsonl", "audit-*.jsonl", It.IsAny<bool>()))
            .Returns([datedPath]);
        fsMock2
            .Setup(fs => fs.ReadAllTextAsync(datedPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstLine);

        fsMock2
            .Setup(fs => fs.AppendAllTextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, line, _) => captured2.Add(line))
            .Returns(Task.CompletedTask);
        fsMock2
            .Setup(fs => fs.CombinePath(It.IsAny<string[]>()))
            .Returns<string[]>(parts => string.Join("/", parts));
        fsMock2.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));

        var sut2 = new HmacAuditLog(fsMock2.Object, timeProv, NullLogger<HmacAuditLog>.Instance);

        // ── Act ──
        await sut2.LogAsync(AuditEvent.ModelCall, "user-init", "Second entry");

        // ── Assert ──
        var (secondSigned, secondEntry) = Parse(captured2[0]);
        secondEntry.Seq.Should().Be(2);
        secondEntry.PrevHash.Should().Be(firstSigned.EntryHash);
        // Verify the new entry is correctly signed with the restored key
        sut2.VerifyEntry(secondSigned).Should().BeTrue();
    }
}

/// <summary>
/// A deterministic <see cref="TimeProvider"/> for use in unit tests.
/// Returns the fixed timestamp supplied at construction; never advances automatically.
/// </summary>
file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => now;
}
