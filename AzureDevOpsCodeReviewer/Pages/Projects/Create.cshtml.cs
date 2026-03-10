using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AzureDevOpsCodeReviewer.Pages.Projects;

public class CreateModel : PageModel
{
    private readonly IProjectConfigStore _store;
    private readonly ProjectRegistry _registry;

    public CreateModel(IProjectConfigStore store, ProjectRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    [BindProperty]
    public string ProjectKey { get; set; } = string.Empty;

    [BindProperty]
    public ProjectOptions Options { get; set; } = new();

    [BindProperty]
    public string AllowedEventsText { get; set; } = "git.pullrequest.created, git.pullrequest.updated";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        ProjectKey = ProjectKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ProjectKey))
        {
            ModelState.AddModelError(nameof(ProjectKey), "Project key is required.");
            return Page();
        }

        if (_store.ContainsKey(ProjectKey))
        {
            ModelState.AddModelError(nameof(ProjectKey), $"A project with key \"{ProjectKey}\" already exists.");
            return Page();
        }

        Options.Review.AllowedEvents = ParseAllowedEvents(AllowedEventsText);
        _store.Save(ProjectKey, Options);
        await _registry.ReloadAsync(_store);

        TempData["Success"] = $"Project \"{ProjectKey}\" created successfully.";
        return RedirectToPage("Index");
    }

    private static string[] ParseAllowedEvents(string? text) =>
        (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
