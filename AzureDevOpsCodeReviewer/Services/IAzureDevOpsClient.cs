using AzureDevOpsCodeReviewer.Models;

namespace AzureDevOpsCodeReviewer.Services;

public interface IAzureDevOpsClient
{
    Task<PullRequestInfo> GetPullRequestAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetPullRequestFilesAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken);
    Task<string?> GetFileContentAsync(string repositoryId, string path, string sourceRefName, CancellationToken cancellationToken);
    Task CreateGeneralCommentAsync(int pullRequestId, string? repositoryId, string comment, CancellationToken cancellationToken);
}
