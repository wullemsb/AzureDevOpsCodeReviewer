using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AzureDevOpsCodeReviewer.Pages.Projects;

public class IndexModel : PageModel
{
    private readonly IProjectConfigStore _store;
    private readonly ProjectRegistry _registry;

    public IndexModel(IProjectConfigStore store, ProjectRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public IReadOnlyDictionary<string, ProjectOptions> Projects { get; private set; } = new Dictionary<string, ProjectOptions>();

    public void OnGet()
    {
        Projects = _store.GetAll();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string key)
    {
        if (_store.Delete(key))
        {
            await _registry.ReloadAsync(_store);
            TempData["Success"] = $"Project \"{key}\" deleted successfully.";
        }
        else
        {
            TempData["Error"] = $"Project \"{key}\" not found.";
        }

        return RedirectToPage();
    }
}
