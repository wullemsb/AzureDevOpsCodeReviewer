using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class AzureDevOpsClient
{
    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsOptions _options;
    private readonly ILogger<AzureDevOpsClient> _logger;

    public AzureDevOpsClient(HttpClient httpClient, IOptions<AzureDevOpsOptions> options, ILogger<AzureDevOpsClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri($"{_options.BaseUrl.TrimEnd('/')}/{_options.Organization}/");

        if (!string.IsNullOrWhiteSpace(_options.Pat))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.Pat}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var url = $"{_options.Project}/_apis/git/repositories/{repo}/pullRequests/{pullRequestId}?api-version=7.1-preview.1";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
        var sourceRef = root.TryGetProperty("sourceRefName", out var sourceElement) ? sourceElement.GetString() ?? string.Empty : string.Empty;
        var targetRef = root.TryGetProperty("targetRefName", out var targetElement) ? targetElement.GetString() ?? string.Empty : string.Empty;
        var repoId = root.TryGetProperty("repository", out var repoElement) && repoElement.TryGetProperty("id", out var repoIdElement)
            ? repoIdElement.GetString() ?? repo
            : repo;

        var webUrl = string.Empty;
        if (root.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web) && web.TryGetProperty("href", out var href))
        {
            webUrl = href.GetString() ?? string.Empty;
        }

        return new PullRequestInfo
        {
            PullRequestId = pullRequestId,
            Title = title,
            SourceRefName = sourceRef,
            TargetRefName = targetRef,
            RepositoryId = repoId,
            WebUrl = webUrl
        };
    }

    public async Task<IReadOnlyList<string>> GetPullRequestFilesAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var iterationId = await GetLatestIterationIdAsync(pullRequestId, repo, cancellationToken);
        if (iterationId == 0)
        {
            return Array.Empty<string>();
        }

        var url = $"{_options.Project}/_apis/git/repositories/{repo}/pullRequests/{pullRequestId}/iterations/{iterationId}/changes?api-version=7.1-preview.1&$top=1000";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("changes", out var changesElement) && changesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in changesElement.EnumerateArray())
            {
                if (!change.TryGetProperty("item", out var item))
                {
                    continue;
                }

                if (item.TryGetProperty("gitObjectType", out var typeElement) && typeElement.GetString() == "tree")
                {
                    continue;
                }

                if (item.TryGetProperty("path", out var pathElement))
                {
                    var path = pathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        changes.Add(path);
                    }
                }
            }
        }

        return changes.ToList();
    }

    public async Task<string?> GetFileContentAsync(string repositoryId, string path, string sourceRefName, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var branch = NormalizeBranchName(sourceRefName);
        if (string.IsNullOrWhiteSpace(branch))
        {
            _logger.LogWarning("Missing source branch for file {Path}", path);
            return null;
        }

        var url = $"{_options.Project}/_apis/git/repositories/{repo}/items?path={Uri.EscapeDataString(path)}&versionDescriptor.version={Uri.EscapeDataString(branch)}&versionDescriptor.versionType=branch&includeContent=true&api-version=7.1-preview.1";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch file {Path}: {Status}", path, response.StatusCode);
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString();
            }

            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task CreateGeneralCommentAsync(int pullRequestId, string? repositoryId, string comment, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var url = $"{_options.Project}/_apis/git/repositories/{repo}/pullRequests/{pullRequestId}/threads?api-version=7.1-preview.1";

        var payload = new
        {
            comments = new[]
            {
                new { content = comment, commentType = 1 }
            },
            status = 1
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetLatestIterationIdAsync(int pullRequestId, string repositoryId, CancellationToken cancellationToken)
    {
        var url = $"{_options.Project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/iterations?api-version=7.1-preview.1";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var maxId = 0;
        if (doc.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var iteration in values.EnumerateArray())
            {
                if (iteration.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
                {
                    maxId = Math.Max(maxId, id);
                }
            }
        }

        return maxId;
    }

    private string ResolveRepositoryId(string? repositoryId)
    {
        return string.IsNullOrWhiteSpace(repositoryId) ? _options.RepositoryId : repositoryId;
    }

    private static string NormalizeBranchName(string sourceRefName)
    {
        if (string.IsNullOrWhiteSpace(sourceRefName))
        {
            return string.Empty;
        }

        const string prefix = "refs/heads/";
        return sourceRefName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? sourceRefName[prefix.Length..]
            : sourceRefName;
    }
}
