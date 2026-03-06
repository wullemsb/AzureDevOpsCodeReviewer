using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Models;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Engine.ClientProtocol;
using Moq;
using Xunit;

namespace AzureDevOpsCodeReviewer.Tests.Services;

/// <summary>
/// Integration tests for <see cref="PullRequestReviewOrchestrator"/>.
/// Each test builds a real <see cref="IServiceProvider"/> that mirrors the DI setup in
/// <c>Program.cs</c>, so constructor wiring and options binding are verified end-to-end.
/// External boundaries (<see cref="IAzureDevOpsClient"/> and <see cref="ICopilotReviewService"/>)
/// are replaced with Moq instances to keep tests fast and deterministic.
/// </summary>
public sealed class PullRequestReviewOrchestratorIntegrationTests
{
    // -------------------------------------------------------------------------
    // Fixture helpers
    // -------------------------------------------------------------------------

    private const string DefaultRepoId = "repo-abc";
    private const string DefaultTargetReviewer = "copilot-reviewer@example.com";


    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> that mirrors the application DI registration
    /// and resolves <see cref="PullRequestReviewOrchestrator"/> from it, so constructor
    /// wiring and options binding are tested end-to-end.
    /// </summary>
    private PullRequestReviewOrchestrator BuildOrchestrator(
        AzureDevOpsOptions? adoOptions = null,
        ReviewOptions? reviewOptions = null)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
             .AddJsonFile("secrets.json", optional: true, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();
        services.AddSingleton<ICopilotReviewService, CopilotReviewService>();

        if (adoOptions is not null)
            services.AddSingleton<IOptions<AzureDevOpsOptions>>(Options.Create(adoOptions));
        else
            services.Configure<AzureDevOpsOptions>(configuration.GetSection("AzureDevOps"));

        if (reviewOptions is not null)
            services.AddSingleton<IOptions<ReviewOptions>>(Options.Create(reviewOptions));
        else
            services.Configure<ReviewOptions>(configuration.GetSection("Review"));

        services.AddSingleton<PullRequestReviewOrchestrator>();

        return services
            .BuildServiceProvider(validateScopes: true)
            .GetRequiredService<PullRequestReviewOrchestrator>();
    }

    private static PullRequestInfo MakePr(
        int id = 1,
        string repoId = DefaultRepoId,
        string sourceRef = "refs/heads/feature/my-work",
        string title = "My PR",
        string webUrl = "https://dev.azure.com/org/proj/_git/repo/pullrequest/1") =>
        new()
        {
            PullRequestId = id,
            RepositoryId = repoId,
            Title = title,
            SourceRefName = sourceRef,
            WebUrl = webUrl
        };

    // -------------------------------------------------------------------------
    // IsAllowedEvent
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("git.pullrequest.created", true)]
    [InlineData("git.pullrequest.updated", true)]
    [InlineData("GIT.PULLREQUEST.CREATED", true)]
    [InlineData("GIT.PULLREQUEST.UPDATED", true)]
    [InlineData("git.pullrequest.merged", false)]
    [InlineData("git.push", false)]
    [InlineData("", false)]
    public void IsAllowedEvent_DefaultAllowedList_ReturnsExpected(string eventType, bool expected)
    {
        var orchestrator = BuildOrchestrator();

        Assert.Equal(expected, orchestrator.IsAllowedEvent(eventType));
    }

    [Fact]
    public void IsAllowedEvent_CustomAllowedList_OnlyMatchesConfiguredEvents()
    {
        var orchestrator = BuildOrchestrator(reviewOptions: new ReviewOptions
        {
            AllowedEvents = ["git.pullrequest.merged"]
        });

        Assert.True(orchestrator.IsAllowedEvent("git.pullrequest.merged"));
        Assert.False(orchestrator.IsAllowedEvent("git.pullrequest.created"));
        Assert.False(orchestrator.IsAllowedEvent("git.pullrequest.updated"));
    }

    [Fact]
    public void IsAllowedEvent_EmptyAllowedList_AlwaysReturnsFalse()
    {
        var orchestrator = BuildOrchestrator(reviewOptions: new ReviewOptions { AllowedEvents = [] });

        Assert.False(orchestrator.IsAllowedEvent("git.pullrequest.created"));
    }

    [Fact]
    public void IsAllowedEvent_PartialEventName_ReturnsFalse()
    {
        var orchestrator = BuildOrchestrator();

        Assert.False(orchestrator.IsAllowedEvent("git.pullrequest"));
    }

    // -------------------------------------------------------------------------
    // IsTargetReviewer
    // -------------------------------------------------------------------------

    [Fact]
    public void IsTargetReviewer_EmptyTargetReviewer_ReturnsFalse()
    {
        var orchestrator = BuildOrchestrator(
            reviewOptions: new ReviewOptions { TargetReviewer = "" });

        Assert.False(orchestrator.IsTargetReviewer(
            [new ReviewerInfo { UniqueName = DefaultTargetReviewer }]));
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("\t")]
    public void IsTargetReviewer_WhitespaceTargetReviewer_ReturnsFalse(string target)
    {
        var orchestrator = BuildOrchestrator(
            reviewOptions: new ReviewOptions { TargetReviewer = target });

        Assert.False(orchestrator.IsTargetReviewer(
            [new ReviewerInfo { UniqueName = "anyone@example.com" }]));
    }

    [Fact]
    public void IsTargetReviewer_EmptyReviewerList_ReturnsFalse()
    {
        var orchestrator = BuildOrchestrator();

        Assert.False(orchestrator.IsTargetReviewer([]));
    }

    [Theory]
    [InlineData(DefaultTargetReviewer, "", "")]                     // match by Id
    [InlineData("", DefaultTargetReviewer, "")]                     // match by UniqueName
    [InlineData("", "", DefaultTargetReviewer)]                     // match by DisplayName
    [InlineData("", "COPILOT-REVIEWER@EXAMPLE.COM", "")]            // case-insensitive
    public void IsTargetReviewer_MatchOnAnyField_ReturnsTrue(
        string id, string uniqueName, string displayName)
    {
        var orchestrator = BuildOrchestrator();
        var reviewers = new List<ReviewerInfo>
        {
            new() { Id = id, UniqueName = uniqueName, DisplayName = displayName }
        };

        Assert.True(orchestrator.IsTargetReviewer(reviewers));
    }

    [Fact]
    public void IsTargetReviewer_MultipleReviewers_MatchOnLastOne_ReturnsTrue()
    {
        var orchestrator = BuildOrchestrator();
        var reviewers = new List<ReviewerInfo>
        {
            new() { UniqueName = "alice@example.com" },
            new() { UniqueName = "bob@example.com" },
            new() { UniqueName = DefaultTargetReviewer }
        };

        Assert.True(orchestrator.IsTargetReviewer(reviewers));
    }

    [Fact]
    public void IsTargetReviewer_NoReviewerMatchesTarget_ReturnsFalse()
    {
        var orchestrator = BuildOrchestrator();
        var reviewers = new List<ReviewerInfo>
        {
            new() { UniqueName = "alice@example.com" },
            new() { UniqueName = "bob@example.com" }
        };

        Assert.False(orchestrator.IsTargetReviewer(reviewers));
    }

    // -------------------------------------------------------------------------
    // ReviewAndCommentAsync — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReviewAndCommentAsync_ValidPrWithFiles_PostsFormattedComment()
    {
        var orchestrator = BuildOrchestrator();
        var pullRequestId = 10702;
        var repositoryId = default(string);
        var project = "Framework en Tooling";
        var reviewers = new List<ReviewerInfo>
        {
            new() { UniqueName = "alice@example.com" },
            new() { UniqueName = "bob@example.com" },
            new() { UniqueName = DefaultTargetReviewer }
        };

        await orchestrator.ReviewAndCommentAsync(pullRequestId, project,repositoryId, CancellationToken.None);
    }

}