using AzureDevOpsCodeReviewer.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class ProjectRegistry : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, ProjectServiceContainer> _projects;

    public ProjectRegistry(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var projects = new Dictionary<string, ProjectServiceContainer>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in configuration.GetSection("Projects").GetChildren())
        {
            var options = child.Get<ProjectOptions>() ?? new ProjectOptions();
            projects[child.Key] = new ProjectServiceContainer(options, loggerFactory);
        }

        _projects = projects;
    }

    public IEnumerable<string> ProjectKeys => _projects.Keys;

    public bool TryGetProject(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProjectServiceContainer? container)
        => _projects.TryGetValue(key, out container);

    public async ValueTask DisposeAsync()
    {
        foreach (var container in _projects.Values)
        {
            await container.DisposeAsync();
        }
    }
}
