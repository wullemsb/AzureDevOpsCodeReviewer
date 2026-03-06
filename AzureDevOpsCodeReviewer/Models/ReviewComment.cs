namespace AzureDevOpsCodeReviewer.Models;

public sealed class ReviewComment
{
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? LineNumber { get; init; }
    public bool IsGeneralComment => string.IsNullOrWhiteSpace(Path) || Path.Equals("(general)", StringComparison.OrdinalIgnoreCase);
}
