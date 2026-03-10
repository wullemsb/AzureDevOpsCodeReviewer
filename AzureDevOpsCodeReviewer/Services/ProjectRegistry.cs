using AzureDevOpsCodeReviewer.Config;
using Microsoft.Extensions.Logging;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class ProjectRegistry : IAsyncDisposable
{
    private readonly Dictionary<string, ProjectServiceContainer> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;

    public ProjectRegistry(IProjectConfigStore configStore, ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;

        foreach (var (key, options) in configStore.GetAll())
        {
            _projects[key] = new ProjectServiceContainer(options, loggerFactory);
        }
    }

    public IEnumerable<string> ProjectKeys => _projects.Keys;

    public bool TryGetProject(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProjectServiceContainer? container)
        => _projects.TryGetValue(key, out container);

    public async Task ReloadAsync(IProjectConfigStore configStore)
    {
        foreach (var container in _projects.Values)
        {
            await container.DisposeAsync();
        }

        _projects.Clear();

        foreach (var (key, options) in configStore.GetAll())
        {
            _projects[key] = new ProjectServiceContainer(options, _loggerFactory);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var container in _projects.Values)
        {
            await container.DisposeAsync();
        }
    }
}
