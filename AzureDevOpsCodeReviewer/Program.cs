using Azure.Monitor.OpenTelemetry.AspNetCore;
using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("secrets.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Configure OpenTelemetry with Application Insights
builder.Services.AddOpenTelemetry()
    .WithTracing(tracingBuilder =>
    {
        tracingBuilder.AddAspNetCoreInstrumentation();
    })
    .UseAzureMonitor();

builder.Services.Configure<AzureDevOpsOptions>(builder.Configuration.GetSection("AzureDevOps"));
builder.Services.Configure<CopilotOptions>(builder.Configuration.GetSection("Copilot"));
builder.Services.Configure<ReviewOptions>(builder.Configuration.GetSection("Review"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhook"));

builder.Services.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();
builder.Services.AddSingleton<AzureDevOpsWebhookParser>();
builder.Services.AddSingleton<WebhookRequestGuard>();
builder.Services.AddSingleton<ICopilotReviewService, CopilotReviewService>();
builder.Services.AddSingleton<PullRequestReviewOrchestrator>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/webhook", async (HttpRequest request,
    AzureDevOpsWebhookParser parser,
    WebhookRequestGuard guard,
    PullRequestReviewOrchestrator orchestrator,
    IOptions<AzureDevOpsOptions> adoOptions,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Webhook");
    if (!guard.IsAuthorized(request))
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    if (!parser.TryParsePayload(body, out var payload, out var error))
    {
        logger.LogWarning("Webhook parsing failed: {Error}", error);
        return Results.BadRequest(new { error });
    }

    if (!orchestrator.IsAllowedEvent(payload.EventType))
    {
        return Results.Accepted();
    }

    //TODO: check if the correct reviewer is assigned
    if (!orchestrator.IsTargetReviewer(payload.Reviewers))
    {
        return Results.Accepted();
    }

    _ = Task.Run(() => orchestrator.ReviewAndCommentAsync(payload.PullRequestId,payload.ProjectName, payload.RepositoryId, CancellationToken.None));
    logger.LogInformation("Queued review for PR {PullRequestId}", payload.PullRequestId);
    return Results.Accepted();
});

app.Run();
