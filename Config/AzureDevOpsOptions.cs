namespace AzureDevOpsCodeReviewer.Config;

public sealed class AzureDevOpsOptions
{
    public string Organization { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string RepositoryId { get; init; } = string.Empty;
    public string Pat { get; init; } = string.Empty;
    public string TargetReviewer { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://dev.azure.com";
}
