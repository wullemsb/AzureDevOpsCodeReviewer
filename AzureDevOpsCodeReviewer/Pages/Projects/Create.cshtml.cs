using AzureDevOpsCodeReviewer.Config;
using AzureDevOpsCodeReviewer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace AzureDevOpsCodeReviewer.Pages.Projects;

public class CreateModel : PageModel
{
    private readonly IProjectConfigStore _store;
    private readonly ProjectRegistry _registry;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(IProjectConfigStore store, ProjectRegistry registry, ILogger<CreateModel> logger)
    {
        _store = store;
        _registry = registry;
        _logger = logger;
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

        if (_registry.TryGetProject(ProjectKey, out var container))
        {
            try
            {
                var webhookUrl = $"{Request.Scheme}://{Request.Host}/webhook/{ProjectKey}";
                await container.RegisterServiceHooksAsync(webhookUrl, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register service hooks for project {ProjectKey}.", ProjectKey);
                TempData["Warning"] = "Project created, but service hook registration failed. Configure it manually in Azure DevOps.";
            }
        }

        TempData["Success"] = $"Project \"{ProjectKey}\" created successfully.";
        return RedirectToPage("Index");
    }

    private static string[] ParseAllowedEvents(string? text) =>
        (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
