using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly AzureDevOpsOptions _options;
    private readonly ILogger<AzureDevOpsClient> _logger;
    private readonly VssConnection _connection;
    private readonly GitHttpClient _gitClient;

    public AzureDevOpsClient(IOptions<AzureDevOpsOptions> options, ILogger<AzureDevOpsClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        var baseUri = new Uri($"{_options.BaseUrl.TrimEnd('/')}/{_options.Organization}");
        VssCredentials credentials = string.IsNullOrWhiteSpace(_options.Pat)
            ? new VssCredentials()
            : new VssBasicCredential(string.Empty, _options.Pat);

        _connection = new VssConnection(baseUri, credentials);
        _gitClient = _connection.GetClient<GitHttpClient>();
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(int pullRequestId, string project, string? repositoryId, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var pr = await _gitClient.GetPullRequestAsync(project, repo, pullRequestId, cancellationToken: cancellationToken);

        var webUrl = string.Empty;
        if (pr.Links?.Links != null && pr.Links.Links.TryGetValue("web", out var webLink))
        {
            webUrl = (webLink as ReferenceLink)?.Href ?? string.Empty;
        }

        return new PullRequestInfo
        {
            PullRequestId = pr.PullRequestId,
            Title = pr.Title ?? string.Empty,
            SourceRefName = pr.SourceRefName ?? string.Empty,
            TargetRefName = pr.TargetRefName ?? string.Empty,
            RepositoryId = pr.Repository?.Id.ToString() ?? repo,
            WebUrl = webUrl
        };
    }

    public async Task<IReadOnlyList<string>> GetPullRequestFilesAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);

        var iterations = await _gitClient.GetPullRequestIterationsAsync(repo, pullRequestId, cancellationToken: cancellationToken);
        var latestIterationId = iterations.Select(i => i.Id ?? 0).DefaultIfEmpty(0).Max();
        if (latestIterationId == 0)
        {
            return Array.Empty<string>();
        }

        var changes = await _gitClient.GetPullRequestIterationChangesAsync(repo, pullRequestId, latestIterationId, top: 1000, cancellationToken: cancellationToken);

        return changes.ChangeEntries
            .Where(c => c.Item?.GitObjectType != GitObjectType.Tree)
            .Select(c => c.Item?.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string?> GetFileContentAsync(string project, string repositoryId, string path, string sourceRefName, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var branch = NormalizeBranchName(sourceRefName);
        if (string.IsNullOrWhiteSpace(branch))
        {
            _logger.LogWarning("Missing source branch for file {Path}", path);
            return null;
        }

        try
        {
            var versionDescriptor = new GitVersionDescriptor
            {
                VersionType = GitVersionType.Branch,
                Version = branch
            };

            using var stream = await _gitClient.GetItemContentAsync(
                project, repo, path, (string?)null, versionDescriptor: versionDescriptor, cancellationToken: cancellationToken);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch file {Path}", path);
            return null;
        }
    }

    public async Task CreateGeneralCommentAsync(int pullRequestId,string project, string? repositoryId, string comment, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var thread = new GitPullRequestCommentThread
        {
            Comments = [new Comment { Content = comment, CommentType = CommentType.Text }],
            Status = CommentThreadStatus.Active
        };

        await _gitClient.CreateThreadAsync(thread, project, repo, pullRequestId, cancellationToken: cancellationToken);
    }

    public async Task CreateFileCommentAsync(int pullRequestId, string project, string? repositoryId, string filePath, int lineNumber, string comment, CancellationToken cancellationToken)
    {
        var repo = ResolveRepositoryId(repositoryId);
        var thread = new GitPullRequestCommentThread
        {
            Comments = [new Comment { Content = comment, CommentType = CommentType.Text }],
            Status = CommentThreadStatus.Active,
            ThreadContext = new CommentThreadContext
            {
                FilePath = filePath,
                RightFileStart = new CommentPosition { Line = lineNumber, Offset = 1 },
                RightFileEnd = new CommentPosition { Line = lineNumber, Offset = 1 }
            }
        };

        await _gitClient.CreateThreadAsync(thread, project, repo, pullRequestId, cancellationToken: cancellationToken);
    }

    public async Task EnsureServiceHookSubscriptionsAsync(
        string projectName,
        string webhookUrl,
        string[] eventTypes,
        string? tokenHeaderName,
        string? tokenValue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            _logger.LogWarning("ProjectName is not configured; skipping service hook registration.");
            return;
        }

        using var httpClient = CreateAdoHttpClient();
        var orgBaseUrl = $"{_options.BaseUrl.TrimEnd('/')}/{_options.Organization}";

        var projectId = await GetProjectIdAsync(httpClient, orgBaseUrl, projectName, cancellationToken);
        if (projectId is null)
        {
            _logger.LogWarning("Could not resolve project GUID for '{ProjectName}'; skipping service hook registration.", projectName);
            return;
        }

        var existingKeys = await GetExistingSubscriptionKeysAsync(httpClient, orgBaseUrl, cancellationToken);

        foreach (var eventType in eventTypes)
        {
            if (existingKeys.Contains((eventType, webhookUrl)))
            {
                _logger.LogInformation("Service hook for {EventType} -> {WebhookUrl} already exists; skipping.", eventType, webhookUrl);
                continue;
            }

            await CreateServiceHookSubscriptionAsync(httpClient, orgBaseUrl, projectId, eventType, webhookUrl, tokenHeaderName, tokenValue, cancellationToken);
            _logger.LogInformation("Created service hook for {EventType} -> {WebhookUrl}.", eventType, webhookUrl);
        }
    }

    private HttpClient CreateAdoHttpClient()
    {
        var client = new HttpClient();
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.Pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<string?> GetProjectIdAsync(HttpClient httpClient, string orgBaseUrl, string projectName, CancellationToken cancellationToken)
    {
        var url = $"{orgBaseUrl}/_apis/projects/{Uri.EscapeDataString(projectName)}?api-version=7.1";
        var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get project '{ProjectName}': {StatusCode}.", projectName, response.StatusCode);
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
    }

    private async Task<HashSet<(string EventType, string Url)>> GetExistingSubscriptionKeysAsync(
        HttpClient httpClient, string orgBaseUrl, CancellationToken cancellationToken)
    {
        var url = $"{orgBaseUrl}/_apis/hooks/subscriptions?api-version=7.1&publisherId=tfs&consumerId=webHooks";
        var response = await httpClient.GetAsync(url, cancellationToken);
        var keys = new HashSet<(string, string)>();
        if (!response.IsSuccessStatusCode)
            return keys;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("value", out var valueEl))
            return keys;

        foreach (var item in valueEl.EnumerateArray())
        {
            var eventType = item.TryGetProperty("eventType", out var etEl) ? etEl.GetString() ?? string.Empty : string.Empty;
            var hookUrl = string.Empty;
            if (item.TryGetProperty("consumerInputs", out var ciEl) && ciEl.TryGetProperty("url", out var urlEl))
                hookUrl = urlEl.GetString() ?? string.Empty;

            if (!string.IsNullOrEmpty(eventType) && !string.IsNullOrEmpty(hookUrl))
                keys.Add((eventType, hookUrl));
        }

        return keys;
    }

    private async Task CreateServiceHookSubscriptionAsync(
        HttpClient httpClient, string orgBaseUrl, string projectId,
        string eventType, string webhookUrl,
        string? tokenHeaderName, string? tokenValue,
        CancellationToken cancellationToken)
    {
        var consumerInputs = new Dictionary<string, string>
        {
            ["url"] = webhookUrl,
            ["resourceDetailsToSend"] = "All",
            ["messagesToSend"] = "None",
            ["detailedMessagesToSend"] = "None"
        };

        if (!string.IsNullOrWhiteSpace(tokenHeaderName) && !string.IsNullOrWhiteSpace(tokenValue))
            consumerInputs["httpHeaders"] = $"{tokenHeaderName}: {tokenValue}";

        var body = JsonSerializer.Serialize(new
        {
            publisherId = "tfs",
            eventType,
            resourceVersion = "1.0",
            consumerId = "webHooks",
            consumerActionId = "httpRequest",
            publisherInputs = new Dictionary<string, string> { ["projectId"] = projectId },
            consumerInputs
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(
            $"{orgBaseUrl}/_apis/hooks/subscriptions?api-version=7.1", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to create service hook for '{eventType}': {(int)response.StatusCode} - {errorBody}");
        }
    }

    private string ResolveRepositoryId(string? repositoryId) =>
        string.IsNullOrWhiteSpace(repositoryId) ? _options.RepositoryId : repositoryId;

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
