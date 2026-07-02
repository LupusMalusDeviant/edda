using Edda.AKG.Parser;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// Hosted service that populates <c>:WorldKnowledge</c> and <c>:Rule</c> nodes
/// on application startup, then rebuilds any missing embeddings.
/// Delegates to <see cref="WorldKnowledgeSeeder.SeedIfEmptyAsync"/> for world knowledge and
/// <see cref="RuleLoader.LoadFromDirectoryAsync"/> for knowledge rules (idempotent MERGE).
/// Also seeds core domains (<c>tools</c>, <c>custom-tools</c>, and toolbox sub-domains)
/// via <see cref="ICypherExecutor"/>.
/// </summary>
internal sealed class WorldKnowledgeSeedHostedService : IHostedService
{
    private const string WorldKnowledgeDirectory = "knowledge/world";
    private const string KnowledgeDirectory = "knowledge";
    private const string KnowledgeSeedDirectory = "knowledge.seed";

    private readonly IWorldKnowledgeSeeder _seeder;
    private readonly IRuleLoader _ruleLoader;
    private readonly IKnowledgeGraph _knowledgeGraph;
    private readonly IFileSystem _fileSystem;
    private readonly ICypherExecutor _cypher;
    private readonly IBackgroundWorkQueue _backgroundWorkQueue;
    private readonly ILogger<WorldKnowledgeSeedHostedService> _logger;

    public WorldKnowledgeSeedHostedService(
        IWorldKnowledgeSeeder seeder,
        IRuleLoader ruleLoader,
        IKnowledgeGraph knowledgeGraph,
        IFileSystem fileSystem,
        ICypherExecutor cypher,
        IBackgroundWorkQueue backgroundWorkQueue,
        ILogger<WorldKnowledgeSeedHostedService> logger)
    {
        _seeder = seeder;
        _ruleLoader = ruleLoader;
        _knowledgeGraph = knowledgeGraph;
        _fileSystem = fileSystem;
        _cypher = cypher;
        _backgroundWorkQueue = backgroundWorkQueue;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "WorldKnowledgeSeedHostedService starting; seeding world knowledge + rules | {Component}", "AKG");

        // Populate the knowledge/ directory from the bundled knowledge.seed/ when it is empty. Container
        // deployments (e.g. Coolify) mount knowledge/ as a fresh, empty volume; without this copy the
        // baseline rules/world files are absent and every seed step below finds nothing to load.
        try
        {
            var copied = await SeedKnowledgeDirectoryIfEmptyAsync(cancellationToken).ConfigureAwait(false);
            if (copied > 0)
                _logger.LogInformation(
                    "Knowledge directory self-seeded from bundle: {Count} files copied into '{Dir}' | {Component}",
                    copied, KnowledgeDirectory, "AKG");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Knowledge directory self-seed from bundle failed | {Component}", "AKG");
        }

        var seeded = await _seeder.SeedIfEmptyAsync(WorldKnowledgeDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (seeded > 0)
            _logger.LogInformation(
                "World knowledge seeding complete: {Count} nodes seeded | {Component}", seeded, "AKG");

        try
        {
            // Seed the baseline knowledge/ rules only into a fresh (empty) graph. Re-MERGEing them on every
            // start would resurrect rules the user deleted via the UI ("delete doesn't stick"). Baseline
            // updates can be applied explicitly via the reload endpoint/button.
            var stats = await _knowledgeGraph.GetStatsAsync(cancellationToken).ConfigureAwait(false);
            var existingRules = stats?.TotalRules ?? 0;
            if (existingRules == 0)
            {
                var rulesLoaded = await _ruleLoader.LoadFromDirectoryAsync(KnowledgeDirectory, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "Baseline rules loaded into empty graph: {Count} from {Dir} | {Component}",
                    rulesLoaded, KnowledgeDirectory, "AKG");
            }
            else
            {
                _logger.LogInformation(
                    "Graph already has {Count} rules — skipping baseline reload (user content preserved) | {Component}",
                    existingRules, "AKG");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Baseline rule reload check failed on startup | {Component}", "AKG");
        }

        // Seed core domains for tool documentation (idempotent MERGE).
        try
        {
            await SeedToolDomainsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Tool domain seeding failed on startup — tool domain may be missing | {Component}", "AKG");
        }

        // Invalidate superseded rules in the background — must NOT block StartAsync. Embedding itself is
        // owned by EmbeddingBackfillHostedService, which drains the corpus resiliently and resumes after
        // restarts, so we no longer kick off a one-shot rebuild here (the old one aborted after a few
        // provider failures and left most of a large corpus unembedded). The work is enqueued on the
        // supervised background queue so it is cancelled on shutdown instead of detached via Task.Run.
        _backgroundWorkQueue.Enqueue(async ct =>
        {
            try
            {
                await _knowledgeGraph.InvalidateSupersededRulesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Superseded-rule invalidation failed on startup | {Component}", "AKG");
            }
        }, "superseded-rule invalidation");
    }

    /// <summary>
    /// Copies the bundled baseline knowledge from <c>knowledge.seed/</c> into <c>knowledge/</c> when the
    /// latter is empty or missing. Container images stage the baseline under <c>knowledge.seed/</c> so a
    /// freshly mounted (empty) <c>knowledge/</c> volume can be populated on first start. Existing content
    /// is preserved — the copy only runs when <c>knowledge/</c> contains no files — so user edits and the
    /// "delete sticks across restart" guarantee are not affected.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of files copied (0 when the bundle is absent or <c>knowledge/</c> already has content).</returns>
    internal async Task<int> SeedKnowledgeDirectoryIfEmptyAsync(CancellationToken ct)
    {
        var knowledgeFull = _fileSystem.GetFullPath(KnowledgeDirectory);
        var seedFull = _fileSystem.GetFullPath(KnowledgeSeedDirectory);

        if (!_fileSystem.DirectoryExists(seedFull))
            return 0;

        if (_fileSystem.DirectoryExists(knowledgeFull)
            && _fileSystem.EnumerateFiles(knowledgeFull, "*", recursive: true).Any())
            return 0;

        _fileSystem.EnsureDirectoryExists(knowledgeFull);

        var copied = 0;
        // Materialise before copying: writing into knowledge/ must not perturb the source enumeration.
        var sourceFiles = _fileSystem.EnumerateFiles(seedFull, "*", recursive: true).ToList();
        foreach (var sourceFile in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Re-root the source path under knowledge/ while preserving its sub-directory layout,
            // using only IFileSystem primitives (no System.IO.Path).
            var relative = sourceFile[seedFull.Length..].Replace('\\', '/').TrimStart('/');
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var targetDir = segments.Length == 1
                ? knowledgeFull
                : _fileSystem.CombinePath([knowledgeFull, .. segments[..^1]]);
            _fileSystem.EnsureDirectoryExists(targetDir);

            var targetFile = _fileSystem.CombinePath([knowledgeFull, .. segments]);
            var bytes = await _fileSystem.ReadAllBytesAsync(sourceFile, ct).ConfigureAwait(false);
            await _fileSystem.WriteAllBytesAsync(targetFile, bytes, ct).ConfigureAwait(false);
            copied++;
        }

        return copied;
    }

    /// <summary>
    /// All toolbox sub-domains under the "tools" parent domain.
    /// Each entry maps a domain name to a human-readable label.
    /// </summary>
    internal static readonly IReadOnlyList<(string Name, string Label)> ToolboxDomains =
    [
        ("tools.browser", "Browser-Automatisierung"),
        ("tools.code", "Code-Ausführung"),
        ("tools.custom", "Benutzerdefinierte Tools (Verwaltung)"),
        ("tools.devops", "DevOps & Docker"),
        ("tools.files", "Dateiverwaltung"),
        ("tools.knowledge", "Wissen & AKG"),
        ("tools.memory", "Gedächtnis & Nutzerdaten"),
        ("tools.multiagent", "Multi-Agent & Forschung"),
        ("tools.scheduling", "Zeitplanung & Benachrichtigung"),
        ("tools.security", "Sicherheit & Credentials"),
        ("tools.web", "Web-Zugriff"),
    ];

    /// <summary>
    /// Seeds the "tools" parent domain, "custom-tools" subdomain,
    /// and all toolbox sub-domains (e.g. "tools.web", "tools.browser").
    /// Uses <see cref="ICypherExecutor"/> directly because <see cref="IDomainManager.CreateDomainAsync"/>
    /// hardcodes <c>isCore = false</c>, but these are system domains that must not be deletable.
    /// </summary>
    internal async Task SeedToolDomainsAsync(CancellationToken ct)
    {
        // Seed parent + custom-tools
        await _cypher.ExecuteAsync(
            """
            MERGE (t:Domain {name: 'tools'})
            SET t.label = 'Tool-Dokumentation',
                t.isCore = true,
                t.description = 'Dokumentation aller Agent-Tools (built-in und custom)'
            WITH t
            MERGE (ct:Domain {name: 'custom-tools'})
            SET ct.label = 'Benutzerdefinierte Tools',
                ct.isCore = true,
                ct.description = 'Vom Benutzer erstellte Custom-Tools'
            WITH t, ct
            MERGE (t)-[:HAS_SUBDOMAIN]->(ct)
            """,
            ct: ct).ConfigureAwait(false);

        // Seed all toolbox sub-domains under "tools"
        foreach (var (name, label) in ToolboxDomains)
        {
            await _cypher.ExecuteAsync(
                """
                MERGE (t:Domain {name: 'tools'})
                WITH t
                MERGE (tb:Domain {name: $name})
                SET tb.label = $label, tb.isCore = true,
                    tb.description = 'Toolbox: ' + $label
                MERGE (t)-[:HAS_SUBDOMAIN]->(tb)
                """,
                new { name, label },
                ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Tool domains seeded: 'tools' + 'custom-tools' + {Count} toolboxes | {Component}",
            ToolboxDomains.Count, "AKG");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
