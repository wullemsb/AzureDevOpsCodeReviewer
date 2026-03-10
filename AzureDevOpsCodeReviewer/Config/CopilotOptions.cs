namespace AzureDevOpsCodeReviewer.Config;

public sealed class CopilotOptions
{
    public string Model { get; set; } = "gpt-5";
    public string? GitHubToken { get; set; }
    public string? CliPath { get; set; }
}
