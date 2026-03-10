namespace AzureDevOpsCodeReviewer.Config;

public sealed class WebhookOptions
{
    public string? Token { get; set; }
    public string TokenHeaderName { get; set; } = "X-Webhook-Token";
}
