using AtoZClinical.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Vendor;

public class SubscriptionsModel : PageModel
{
    private readonly SaasSubscriptionService _subscriptions;

    public SubscriptionsModel(SaasSubscriptionService subscriptions) => _subscriptions = subscriptions;

    public IReadOnlyList<SubscriptionReportRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() =>
        Rows = await _subscriptions.GetSubscriptionReportAsync();
}
