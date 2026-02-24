namespace AzureDevOpsCodeReviewer.Models;

public sealed class ReviewComment
{
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
