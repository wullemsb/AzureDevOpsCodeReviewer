namespace AzureDevOpsCodeReviewer.Config;

public sealed class WebhookOptions
{
    public string? Token { get; init; }
    public string TokenHeaderName { get; init; } = "X-Webhook-Token";
}
