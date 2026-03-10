using Azure.Monitor.OpenTelemetry.AspNetCore;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

// Project configuration store (JSON file-based persistence)
var projectsFilePath = Path.Combine(builder.Environment.ContentRootPath, "projects.json");
builder.Services.AddSingleton<IProjectConfigStore>(new JsonProjectConfigStore(projectsFilePath));

builder.Services.AddSingleton<AzureDevOpsWebhookParser>();
builder.Services.AddSingleton<ProjectRegistry>();

// Add Razor Pages
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/webhook/{projectKey}", async (string projectKey,
    HttpRequest request,
    ProjectRegistry registry,
    AzureDevOpsWebhookParser parser,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Webhook");

    if (!registry.TryGetProject(projectKey, out var project))
    {
        logger.LogWarning("Unknown project key: {ProjectKey}", projectKey);
        return Results.NotFound();
    }

    if (!project.Guard.IsAuthorized(request))
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

    if (!project.Orchestrator.IsAllowedEvent(payload.EventType))
    {
        return Results.Accepted();
    }

    //TODO: check if the correct reviewer is assigned
    if (!project.Orchestrator.IsTargetReviewer(payload.Reviewers))
    {
        return Results.Accepted();
    }

    _ = Task.Run(() => project.Orchestrator.ReviewAndCommentAsync(payload.PullRequestId, payload.ProjectName, payload.RepositoryId, CancellationToken.None));
    logger.LogInformation("Queued review for PR {PullRequestId} (project: {ProjectKey})", payload.PullRequestId, projectKey);
    return Results.Accepted();
});

app.MapRazorPages();

app.Run();
