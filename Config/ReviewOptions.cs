namespace AzureDevOpsCodeReviewer.Config;

public sealed class ReviewOptions
{
    public int MaxFiles { get; init; } = 20;
    public int MaxFileSizeBytes { get; init; } = 20000;
    public int ReviewCooldownMinutes { get; init; } = 30;
    public string[] AllowedEvents { get; init; } = ["pullrequest.created", "pullrequest.updated"];
}
