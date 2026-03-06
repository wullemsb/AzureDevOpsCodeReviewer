namespace AzureDevOpsCodeReviewer.Models;

public sealed class FileSnapshot
{
    public string Path { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}
