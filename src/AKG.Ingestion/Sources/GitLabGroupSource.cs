using System.Runtime.CompilerServices;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Sources;

/// <summary>
/// Ingestion source that scans <em>all</em> repositories in a GitLab group (including subgroups). It
/// resolves the group's project clone URLs via <see cref="IGitLabClient"/> and then ingests each
/// repository through the regular <see cref="GitMarkdownSource"/> (clone → scan → cleanup per repo).
/// The group path is taken from <c>IngestionSourceConfig.Settings["group"]</c>.
/// </summary>
public sealed class GitLabGroupSource : IIngestionSource
{
    /// <summary>Settings key holding the GitLab group path to scan.</summary>
    public const string GroupSettingKey = "group";

    /// <summary>Settings key holding the GitLab base URL the group lives on.</summary>
    public const string BaseUrlSettingKey = "gitlabBaseUrl";

    private readonly IGitLabClientFactory _gitLabFactory;
    private readonly GitMarkdownSource _gitSource;

    /// <summary>Initializes a new instance of the <see cref="GitLabGroupSource"/> class.</summary>
    /// <param name="gitLabFactory">Factory building the GitLab client for the configured instance (base URL + token).</param>
    /// <param name="gitSource">Per-repository Git source reused for clone + scan.</param>
    public GitLabGroupSource(IGitLabClientFactory gitLabFactory, GitMarkdownSource gitSource)
    {
        _gitLabFactory = gitLabFactory;
        _gitSource = gitSource;
    }

    /// <inheritdoc />
    public string SourceKind => "gitlab-group";

    /// <inheritdoc />
    public async IAsyncEnumerable<IngestionItem> FetchAsync(
        IngestionSourceConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!config.Settings.TryGetValue(GroupSettingKey, out var group) || string.IsNullOrWhiteSpace(group))
            yield break;
        if (!config.Settings.TryGetValue(BaseUrlSettingKey, out var baseUrl) || string.IsNullOrWhiteSpace(baseUrl))
            yield break;

        var gitLab = _gitLabFactory.Create(baseUrl, config.Token);
        var repositoryUrls = await gitLab
            .ListGroupProjectCloneUrlsAsync(group, cancellationToken)
            .ConfigureAwait(false);

        foreach (var repositoryUrl in repositoryUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var repoConfig = config with { RepositoryUrl = repositoryUrl };
            await foreach (var item in _gitSource.FetchAsync(repoConfig, cancellationToken).ConfigureAwait(false))
                yield return item;
        }
    }
}
