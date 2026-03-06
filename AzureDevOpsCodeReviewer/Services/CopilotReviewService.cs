using System.Text;
using System.Text.Json;
using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class CopilotReviewService : ICopilotReviewService, IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly CopilotOptions _options;
    private readonly ILogger<CopilotReviewService> _logger;
    private bool _started;

    public CopilotReviewService(IOptions<CopilotOptions> options, ILogger<CopilotReviewService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var clientOptions = new CopilotClientOptions
        {
            GitHubToken = string.IsNullOrWhiteSpace(_options.GitHubToken) ? null : _options.GitHubToken,
            UseLoggedInUser = string.IsNullOrWhiteSpace(_options.GitHubToken),
            CliPath = string.IsNullOrWhiteSpace(_options.CliPath) ? null : _options.CliPath
        };

        _client = new CopilotClient(clientOptions);
    }

    public async Task<IReadOnlyList<ReviewComment>> ReviewAsync(PullRequestInfo pullRequest, IReadOnlyList<FileSnapshot> files, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync();

        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _options.Model,
            Streaming = false
        });

        var prompt = BuildPrompt(pullRequest, files);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var responses = new List<string>();

        using var subscription = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responses.Add(msg.Data.Content ?? string.Empty);
                    break;
                case SessionIdleEvent:
                    done.TrySetResult(true);
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt });
        await done.Task.WaitAsync(cancellationToken);

        var combined = string.Join("\n", responses).Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return Array.Empty<ReviewComment>();
        }

        return ParseComments(combined);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await _client.StopAsync();
        }
    }

    private async Task EnsureStartedAsync()
    {
        if (_started)
        {
            return;
        }

        await _client.StartAsync();
        _started = true;
    }

    private static string BuildPrompt(PullRequestInfo pullRequest, IReadOnlyList<FileSnapshot> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are reviewing a pull request for potential issues. Focus on correctness, security, performance, and edge cases.");
        builder.AppendLine("Return JSON only with two arrays:");
        builder.AppendLine("1. 'overallComments': array of strings for general PR-level feedback");
        builder.AppendLine("2. 'fileComments': array of objects with 'path' (string), 'line' (number), and 'message' (string) for specific code issues");
        builder.AppendLine("Example: {\"overallComments\": [\"Good test coverage\"], \"fileComments\": [{\"path\": \"file.cs\", \"line\": 42, \"message\": \"Null check needed\"}]}");
        builder.AppendLine("If there are no issues, return {\"overallComments\": [], \"fileComments\": []}");
        builder.AppendLine();
        builder.AppendLine($"PR: {pullRequest.Title}");
        builder.AppendLine($"Source: {pullRequest.SourceRefName}");
        builder.AppendLine($"Target: {pullRequest.TargetRefName}");
        builder.AppendLine();
        builder.AppendLine("Files:");

        foreach (var file in files)
        {
            builder.AppendLine($"--- {file.Path} ---");
            builder.AppendLine(file.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private IReadOnlyList<ReviewComment> ParseComments(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new[] { new ReviewComment { Path = "(general)", Message = content } };
            }

            var results = new List<ReviewComment>();

            if (doc.RootElement.TryGetProperty("overallComments", out var overallArray) && overallArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in overallArray.EnumerateArray())
                {
                    var message = item.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        results.Add(new ReviewComment { Path = "(general)", Message = message.Trim() });
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("fileComments", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fileArray.EnumerateArray())
                {
                    var path = item.TryGetProperty("path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty;
                    var message = item.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? string.Empty : string.Empty;
                    var line = item.TryGetProperty("line", out var lineElement) && lineElement.ValueKind == JsonValueKind.Number
                        ? lineElement.GetInt32()
                        : (int?)null;

                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    results.Add(new ReviewComment { Path = path, Message = message.Trim(), LineNumber = line });
                }
            }

            return results;
        }
        catch (JsonException)
        {
            _logger.LogWarning("Copilot response was not valid JSON.");
            return new[] { new ReviewComment { Path = "(general)", Message = content } };
        }
    }
}
