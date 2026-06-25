using AtoZClinical.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Services;

public sealed class ClinicLicenseMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ClinicLicenseMaintenanceService> _logger;

    public ClinicLicenseMaintenanceService(IServiceProvider services, ILogger<ClinicLicenseMaintenanceService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var subscriptions = scope.ServiceProvider.GetRequiredService<SaasSubscriptionService>();
                var count = await subscriptions.ExpireOverdueSubscriptionsAsync(stoppingToken);
                if (count > 0)
                    _logger.LogInformation("Marked {Count} clinic(s) as expired (subscription date passed).", count);

                var emails = scope.ServiceProvider.GetRequiredService<SubscriptionEmailService>();
                await emails.SendLifecycleEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "License maintenance job failed.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
