namespace AzureDevOpsCodeReviewer.Config;

public sealed class CopilotOptions
{
    public string Model { get; init; } = "gpt-5";
    public string? GitHubToken { get; init; }
    public string? CliPath { get; init; }
}
