using System.Text.Json;
using AzureDevOpsCodeReviewer.Config;

namespace AzureDevOpsCodeReviewer.Services;

public sealed class JsonProjectConfigStore : IProjectConfigStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, ProjectOptions> _projects;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public JsonProjectConfigStore(string filePath)
    {
        _filePath = filePath;
        _projects = Load();
    }

    public IReadOnlyDictionary<string, ProjectOptions> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, ProjectOptions>(_projects, StringComparer.OrdinalIgnoreCase);
        }
    }

    public ProjectOptions? Get(string projectKey)
    {
        lock (_lock)
        {
            return _projects.TryGetValue(projectKey, out var options) ? options : null;
        }
    }

    public void Save(string projectKey, ProjectOptions options)
    {
        lock (_lock)
        {
            _projects[projectKey] = options;
            Persist();
        }
    }

    public bool Delete(string projectKey)
    {
        lock (_lock)
        {
            if (!_projects.Remove(projectKey))
                return false;

            Persist();
            return true;
        }
    }

    public bool ContainsKey(string projectKey)
    {
        lock (_lock)
        {
            return _projects.ContainsKey(projectKey);
        }
    }

    private Dictionary<string, ProjectOptions> Load()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, ProjectOptions>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(_filePath);
        var wrapper = JsonSerializer.Deserialize<ProjectsWrapper>(json, JsonOptions);
        if (wrapper?.Projects is null)
            return new Dictionary<string, ProjectOptions>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, ProjectOptions>(wrapper.Projects, StringComparer.OrdinalIgnoreCase);
    }

    private void Persist()
    {
        var wrapper = new ProjectsWrapper { Projects = _projects };
        var json = JsonSerializer.Serialize(wrapper, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private sealed class ProjectsWrapper
    {
        public Dictionary<string, ProjectOptions> Projects { get; set; } = new();
    }
}
