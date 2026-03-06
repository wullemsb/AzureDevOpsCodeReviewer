namespace AzureDevOpsCodeReviewer.Models;

public sealed class WebhookPayload
{
    public string EventType { get; init; } = string.Empty;
    public int PullRequestId { get; init; }
    public string RepositoryId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public IReadOnlyList<ReviewerInfo> Reviewers { get; init; } = Array.Empty<ReviewerInfo>();
}

public sealed class ReviewerInfo
{
    public string Id { get; init; } = string.Empty;
    public string UniqueName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
