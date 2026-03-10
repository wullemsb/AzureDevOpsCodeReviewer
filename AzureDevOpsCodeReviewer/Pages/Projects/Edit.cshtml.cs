using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AzureDevOpsCodeReviewer.Pages.Projects;

public class EditModel : PageModel
{
    private readonly IProjectConfigStore _store;
    private readonly ProjectRegistry _registry;

    public EditModel(IProjectConfigStore store, ProjectRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    [BindProperty(SupportsGet = true)]
    public string Key { get; set; } = string.Empty;

    [BindProperty]
    public ProjectOptions Options { get; set; } = new();

    [BindProperty]
    public string AllowedEventsText { get; set; } = string.Empty;

    public IActionResult OnGet()
    {
        var existing = _store.Get(Key);
        if (existing is null)
        {
            TempData["Error"] = $"Project \"{Key}\" not found.";
            return RedirectToPage("Index");
        }

        Options = existing;
        AllowedEventsText = string.Join(", ", existing.Review.AllowedEvents);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_store.ContainsKey(Key))
        {
            TempData["Error"] = $"Project \"{Key}\" not found.";
            return RedirectToPage("Index");
        }

        Options.Review.AllowedEvents = ParseAllowedEvents(AllowedEventsText);
        _store.Save(Key, Options);
        await _registry.ReloadAsync(_store);

        TempData["Success"] = $"Project \"{Key}\" updated successfully.";
        return RedirectToPage("Index");
    }

    private static string[] ParseAllowedEvents(string? text) =>
        (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
