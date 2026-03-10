using AzureDevOpsCodeReviewer.Config;

namespace AzureDevOpsCodeReviewer.Services;

public interface IProjectConfigStore
{
    IReadOnlyDictionary<string, ProjectOptions> GetAll();
    ProjectOptions? Get(string projectKey);
    void Save(string projectKey, ProjectOptions options);
    bool Delete(string projectKey);
    bool ContainsKey(string projectKey);
}
