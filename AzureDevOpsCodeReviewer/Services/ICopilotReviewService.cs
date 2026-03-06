using AzureDevOpsCodeReviewer.Models;

namespace AzureDevOpsCodeReviewer.Services;

public interface ICopilotReviewService
{
    Task<IReadOnlyList<ReviewComment>> ReviewAsync(PullRequestInfo pullRequest, IReadOnlyList<FileSnapshot> files, CancellationToken cancellationToken);
}
