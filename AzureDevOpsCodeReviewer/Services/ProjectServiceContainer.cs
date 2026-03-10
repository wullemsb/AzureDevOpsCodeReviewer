using AzureDevOpsCodeReviewer.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class ProjectServiceContainer : IAsyncDisposable
{
    private readonly ICopilotReviewService _copilotService;

    public PullRequestReviewOrchestrator Orchestrator { get; }
    public WebhookRequestGuard Guard { get; }

    public ProjectServiceContainer(ProjectOptions options, ILoggerFactory loggerFactory)
    {
        var adoClient = new AzureDevOpsClient(
            Options.Create(options.AzureDevOps),
            loggerFactory.CreateLogger<AzureDevOpsClient>());

        _copilotService = new CopilotReviewService(
            Options.Create(options.Copilot),
            loggerFactory.CreateLogger<CopilotReviewService>());

        Orchestrator = new PullRequestReviewOrchestrator(
            adoClient,
            _copilotService,
            Options.Create(options.AzureDevOps),
            Options.Create(options.Review),
            loggerFactory.CreateLogger<PullRequestReviewOrchestrator>());

        Guard = new WebhookRequestGuard(Options.Create(options.Webhook));
    }

    public async ValueTask DisposeAsync()
    {
        if (_copilotService is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }
}
