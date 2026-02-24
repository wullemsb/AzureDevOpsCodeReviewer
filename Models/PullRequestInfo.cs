namespace AzureDevOpsCodeReviewer.Models;

public sealed class PullRequestInfo
{
    public int PullRequestId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SourceRefName { get; init; } = string.Empty;
    public string TargetRefName { get; init; } = string.Empty;
    public string RepositoryId { get; init; } = string.Empty;
    public string WebUrl { get; init; } = string.Empty;
}
