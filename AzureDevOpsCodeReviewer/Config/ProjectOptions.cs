namespace AzureDevOpsCodeReviewer.Config;

public sealed class ProjectOptions
{
    public AzureDevOpsOptions AzureDevOps { get; set; } = new();
    public CopilotOptions Copilot { get; set; } = new();
    public ReviewOptions Review { get; set; } = new();
    public WebhookOptions Webhook { get; set; } = new();
}
