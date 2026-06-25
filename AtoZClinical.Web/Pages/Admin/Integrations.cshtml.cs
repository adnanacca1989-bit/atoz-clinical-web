using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Webhooks;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Admin;

public class IntegrationsModel : PageModel
{
    private readonly ClinicContextService _context;
    private readonly ClinicApiKeyService _apiKeys;
    private readonly WebhookSubscriptionService _webhooks;

    public IntegrationsModel(
        ClinicContextService context,
        ClinicApiKeyService apiKeys,
        WebhookSubscriptionService webhooks)
    {
        _context = context;
        _apiKeys = apiKeys;
        _webhooks = webhooks;
    }

    public List<ClinicApiKey> ApiKeys { get; private set; } = [];
    public List<WebhookSubscription> WebhookSubscriptions { get; private set; } = [];
    public string? NewPlainApiKey { get; private set; }
    public string? Message { get; private set; }

    [BindProperty]
    public string NewKeyName { get; set; } = "Default";

    [BindProperty]
    public string WebhookUrl { get; set; } = "";

    [BindProperty]
    public string SelectedWebhookEvents { get; set; } = string.Join(",", WebhookEvents.All);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await _context.RequireOperationalClinicIdAsync();
        if (clinicId is null) return Forbid();

        await LoadAsync(clinicId.Value);
        NewPlainApiKey = TempData["NewPlainApiKey"]?.ToString();
        Message = TempData["Message"]?.ToString();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateKeyAsync()
    {
        var clinicId = await _context.RequireOperationalClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (string.IsNullOrWhiteSpace(NewKeyName))
        {
            Message = "API key name is required.";
            await LoadAsync(clinicId.Value);
            return Page();
        }

        var (_, plain) = await _apiKeys.CreateAsync(clinicId.Value, NewKeyName);
        TempData["NewPlainApiKey"] = plain;
        TempData["Message"] = "Copy the API key now — it will not be shown again.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeKeyAsync(Guid keyId)
    {
        var clinicId = await _context.RequireOperationalClinicIdAsync();
        if (clinicId is null) return Forbid();

        await _apiKeys.RevokeAsync(clinicId.Value, keyId);
        TempData["Message"] = "API key revoked.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddWebhookAsync()
    {
        var clinicId = await _context.RequireOperationalClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out _))
        {
            Message = "Enter a valid webhook URL.";
            await LoadAsync(clinicId.Value);
            return Page();
        }

        var sub = await _webhooks.CreateAsync(clinicId.Value, WebhookUrl, SelectedWebhookEvents);
        TempData["Message"] = $"Webhook created. Signing secret: {sub.Secret}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteWebhookAsync(Guid webhookId)
    {
        var clinicId = await _context.RequireOperationalClinicIdAsync();
        if (clinicId is null) return Forbid();

        await _webhooks.DeleteAsync(clinicId.Value, webhookId);
        TempData["Message"] = "Webhook removed.";
        return RedirectToPage();
    }

    private async Task LoadAsync(Guid clinicId)
    {
        ApiKeys = await _apiKeys.ListAsync(clinicId);
        WebhookSubscriptions = await _webhooks.ListAsync(clinicId);
    }
}
