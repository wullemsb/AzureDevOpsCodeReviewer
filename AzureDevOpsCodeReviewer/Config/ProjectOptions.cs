namespace AzureDevOpsCodeReviewer.Config;

public sealed class ProjectOptions
{
    public AzureDevOpsOptions AzureDevOps { get; init; } = new();
    public CopilotOptions Copilot { get; init; } = new();
    public ReviewOptions Review { get; init; } = new();
    public WebhookOptions Webhook { get; init; } = new();
}
