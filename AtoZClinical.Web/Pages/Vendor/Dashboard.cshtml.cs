using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Vendor;

public class DashboardModel : PageModel
{
    private readonly VendorAnalyticsService _analytics;
    private readonly VendorClinicService _vendor;

    public DashboardModel(VendorAnalyticsService analytics, VendorClinicService vendor)
    {
        _analytics = analytics;
        _vendor = vendor;
    }

    public SaasDashboardSummary Dashboard { get; private set; } = null!;
    public List<Clinic> RecentClinics { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Dashboard = await _analytics.GetDashboardAsync();
        RecentClinics = (await _vendor.ListClinicsAsync()).Take(15).ToList();
    }
}
