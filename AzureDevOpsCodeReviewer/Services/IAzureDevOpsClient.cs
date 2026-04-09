using AzureDevOpsCodeReviewer.Models;

namespace AzureDevOpsCodeReviewer.Services;

public interface IAzureDevOpsClient
{
    Task<PullRequestInfo> GetPullRequestAsync(int pullRequestId, string project, string? repositoryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetPullRequestFilesAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken);
    Task<string?> GetFileContentAsync(string project, string repositoryId, string path, string sourceRefName, CancellationToken cancellationToken);
    Task CreateGeneralCommentAsync(int pullRequestId, string project, string? repositoryId, string comment, CancellationToken cancellationToken);
    Task CreateFileCommentAsync(int pullRequestId,string project, string? repositoryId, string filePath, int lineNumber, string comment, CancellationToken cancellationToken);
    Task EnsureServiceHookSubscriptionsAsync(string projectName, string webhookUrl, string[] eventTypes, string? tokenHeaderName, string? tokenValue, CancellationToken cancellationToken);
}
