using AzureDevOpsCodeReviewer.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class ProjectServiceContainer : IAsyncDisposable
{
    private readonly IAzureDevOpsClient _adoClient;
    private readonly ICopilotReviewService _copilotService;
    private readonly ProjectOptions _options;

    public PullRequestReviewOrchestrator Orchestrator { get; }
    public WebhookRequestGuard Guard { get; }

    public ProjectServiceContainer(ProjectOptions options, ILoggerFactory loggerFactory)
    {
        _options = options;

        _adoClient = new AzureDevOpsClient(
            Options.Create(options.AzureDevOps),
            loggerFactory.CreateLogger<AzureDevOpsClient>());

        _copilotService = new CopilotReviewService(
            Options.Create(options.Copilot),
            loggerFactory.CreateLogger<CopilotReviewService>());

        Orchestrator = new PullRequestReviewOrchestrator(
            _adoClient,
            _copilotService,
            Options.Create(options.AzureDevOps),
            Options.Create(options.Review),
            loggerFactory.CreateLogger<PullRequestReviewOrchestrator>());

        Guard = new WebhookRequestGuard(Options.Create(options.Webhook));
    }

    public Task RegisterServiceHooksAsync(string webhookUrl, CancellationToken cancellationToken) =>
        _adoClient.EnsureServiceHookSubscriptionsAsync(
            _options.AzureDevOps.ProjectName,
            webhookUrl,
            _options.Review.AllowedEvents,
            _options.Webhook.TokenHeaderName,
            _options.Webhook.Token,
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_copilotService is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }
}
