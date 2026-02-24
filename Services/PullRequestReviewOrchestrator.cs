using System.Collections.Concurrent;
using System.Text;
using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class PullRequestReviewOrchestrator
{
    private readonly AzureDevOpsClient _azureDevOps;
    private readonly CopilotReviewService _copilot;
    private readonly AzureDevOpsOptions _adoOptions;
    private readonly ReviewOptions _reviewOptions;
    private readonly ILogger<PullRequestReviewOrchestrator> _logger;
    private readonly ConcurrentDictionary<int, DateTimeOffset> _recentReviews = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public PullRequestReviewOrchestrator(
        AzureDevOpsClient azureDevOps,
        CopilotReviewService copilot,
        IOptions<AzureDevOpsOptions> adoOptions,
        IOptions<ReviewOptions> reviewOptions,
        ILogger<PullRequestReviewOrchestrator> logger)
    {
        _azureDevOps = azureDevOps;
        _copilot = copilot;
        _adoOptions = adoOptions.Value;
        _reviewOptions = reviewOptions.Value;
        _logger = logger;
    }

    public bool IsAllowedEvent(string eventType)
    {
        return _reviewOptions.AllowedEvents.Any(e => string.Equals(e, eventType, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsTargetReviewer(IReadOnlyList<ReviewerInfo> reviewers)
    {
        if (string.IsNullOrWhiteSpace(_adoOptions.TargetReviewer))
        {
            return false;
        }

        foreach (var reviewer in reviewers)
        {
            if (MatchesTarget(reviewer.Id) || MatchesTarget(reviewer.UniqueName) || MatchesTarget(reviewer.DisplayName))
            {
                return true;
            }
        }

        return false;
    }

    public async Task ReviewAndCommentAsync(int pullRequestId, string? repositoryId, CancellationToken cancellationToken)
    {
        if (!TryEnterCooldown(pullRequestId))
        {
            _logger.LogInformation("Skipping PR {PullRequestId} due to cooldown.", pullRequestId);
            return;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var pr = await _azureDevOps.GetPullRequestAsync(pullRequestId, repositoryId, cancellationToken);
            var files = await _azureDevOps.GetPullRequestFilesAsync(pullRequestId, pr.RepositoryId, cancellationToken);

            var snapshots = new List<FileSnapshot>();
            foreach (var filePath in files.Take(_reviewOptions.MaxFiles))
            {
                var content = await _azureDevOps.GetFileContentAsync(pr.RepositoryId, filePath, pr.SourceRefName, cancellationToken);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (content.Length > _reviewOptions.MaxFileSizeBytes)
                {
                    _logger.LogInformation("Skipping large file {Path}", filePath);
                    continue;
                }

                if (content.IndexOf('\0', StringComparison.Ordinal) >= 0)
                {
                    _logger.LogInformation("Skipping binary file {Path}", filePath);
                    continue;
                }

                snapshots.Add(new FileSnapshot { Path = filePath, Content = content });
            }

            if (snapshots.Count == 0)
            {
                _logger.LogInformation("No files to review for PR {PullRequestId}.", pullRequestId);
                return;
            }

            var comments = await _copilot.ReviewAsync(pr, snapshots, cancellationToken);
            if (comments.Count == 0)
            {
                _logger.LogInformation("Copilot returned no review comments for PR {PullRequestId}.", pullRequestId);
                return;
            }

            var commentBody = FormatComments(pr, comments);
            await _azureDevOps.CreateGeneralCommentAsync(pullRequestId, pr.RepositoryId, commentBody, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review failed for PR {PullRequestId}", pullRequestId);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool TryEnterCooldown(int pullRequestId)
    {
        var now = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(_reviewOptions.ReviewCooldownMinutes);

        if (_recentReviews.TryGetValue(pullRequestId, out var lastReview) && now - lastReview < cooldown)
        {
            return false;
        }

        _recentReviews[pullRequestId] = now;
        return true;
    }

    private bool MatchesTarget(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && string.Equals(value, _adoOptions.TargetReviewer, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatComments(PullRequestInfo pullRequest, IReadOnlyList<ReviewComment> comments)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Copilot review for PR #{pullRequest.PullRequestId}: {pullRequest.Title}");
        if (!string.IsNullOrWhiteSpace(pullRequest.WebUrl))
        {
            builder.AppendLine($"PR: {pullRequest.WebUrl}");
        }

        builder.AppendLine();
        foreach (var comment in comments)
        {
            builder.AppendLine($"- {comment.Path}: {comment.Message}");
        }

        return builder.ToString().Trim();
    }
}
