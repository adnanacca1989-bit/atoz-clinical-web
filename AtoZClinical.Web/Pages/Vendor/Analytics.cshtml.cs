using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Vendor;

public class AnalyticsModel : PageModel
{
    private readonly VendorAnalyticsService _analytics;

    public AnalyticsModel(VendorAnalyticsService analytics) => _analytics = analytics;

    public VendorAnalyticsSummary Summary { get; private set; } = null!;
    public SaasDashboardSummary? Dashboard { get; private set; }

    public async Task OnGetAsync()
    {
        Dashboard = await _analytics.GetDashboardAsync();
        Summary = await _analytics.GetSummaryAsync();
    }
}
