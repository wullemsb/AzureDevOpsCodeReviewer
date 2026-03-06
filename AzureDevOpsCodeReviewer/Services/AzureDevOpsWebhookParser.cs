using System.Text.Json;
using AzureDevOpsCodeReviewer.Models;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class AzureDevOpsWebhookParser
{
    public bool TryParsePayload(string body, out WebhookPayload payload, out string error)
    {
        payload = new WebhookPayload();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "Empty request body.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var eventType = root.TryGetProperty("eventType", out var eventElement)
                ? eventElement.GetString() ?? string.Empty
                : string.Empty;

            if (!root.TryGetProperty("resource", out var resource))
            {
                error = "Missing resource payload.";
                return false;
            }

            var prId = 0;
            if (resource.TryGetProperty("pullRequestId", out var prIdElement) && prIdElement.TryGetInt32(out var parsedId))
            {
                prId = parsedId;
            }
            else if (resource.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var parsedAltId))
            {
                prId = parsedAltId;
            }

            if (prId == 0)
            {
                error = "Missing pull request id.";
                return false;
            }

            var repoId = string.Empty;
            var projectId = string.Empty;
            var projectName = string.Empty;
            if (resource.TryGetProperty("repository", out var repo))
            {
                if (repo.TryGetProperty("id", out var repoIdElement))
                    repoId = repoIdElement.GetString() ?? string.Empty;

                if (repo.TryGetProperty("project", out var project))
                {
                    if (project.TryGetProperty("id", out var projIdElement))
                        projectId = projIdElement.GetString() ?? string.Empty;

                    if (project.TryGetProperty("name", out var projNameElement))
                        projectName = projNameElement.GetString() ?? string.Empty;
                }
            }

            var reviewers = new List<ReviewerInfo>();
            if (resource.TryGetProperty("reviewers", out var reviewersElement) && reviewersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var reviewer in reviewersElement.EnumerateArray())
                {
                    reviewers.Add(new ReviewerInfo
                    {
                        Id = reviewer.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        UniqueName = reviewer.TryGetProperty("uniqueName", out var unique) ? unique.GetString() ?? string.Empty : string.Empty,
                        DisplayName = reviewer.TryGetProperty("displayName", out var display) ? display.GetString() ?? string.Empty : string.Empty
                    });
                }
            }

            payload = new WebhookPayload
            {
                EventType = eventType,
                PullRequestId = prId,
                RepositoryId = repoId,
                ProjectId = projectId,
                ProjectName = projectName,
                Reviewers = reviewers
            };

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON payload: {ex.Message}";
            return false;
        }
    }
}
