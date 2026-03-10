namespace AzureDevOpsCodeReviewer.Config;

public sealed class ReviewOptions
{
    public int MaxFiles { get; set; } = 20;
    public int MaxFileSizeBytes { get; set; } = 20000;
    public int ReviewCooldownMinutes { get; set; } = 30;
    public string[] AllowedEvents { get; set; } = ["git.pullrequest.created", "git.pullrequest.updated"];
    public string TargetReviewer { get; set; } = string.Empty;
}
