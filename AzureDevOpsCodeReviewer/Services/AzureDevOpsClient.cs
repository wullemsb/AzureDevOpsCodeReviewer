using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly AzureDevOpsOptions _options;
    private readonly ILogger<AzureDevOpsClient> _logger;
    private readonly GitHttpClient _gitClient;

    public AzureDevOpsClient(IOptions<AzureDevOpsOptions> options, ILogger<AzureDevOpsClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        var baseUri = new Uri($"{_options.BaseUrl.TrimEnd('/')}/{_options.Organization}");
        VssCredentials credentials = string.IsNullOrWhiteSpace(_options.Pat)
            ? new VssCredentials()
            : new VssBasicCredential(string.Empty, _options.Pat);

        var connection = new VssConnection(baseUri, credentials);
        _gitClient = connection.GetClient<GitHttpClient>();
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
