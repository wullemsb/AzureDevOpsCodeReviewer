namespace AzureDevOpsCodeReviewer.Config;

public sealed class AzureDevOpsOptions
{
    public string Organization { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string Pat { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://dev.azure.com";
}
