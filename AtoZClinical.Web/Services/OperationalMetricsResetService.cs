namespace AtoZClinical.Web.Services;

public sealed class OperationalMetricsResetService : BackgroundService
{
    private readonly OperationalMetrics _metrics;

    public OperationalMetricsResetService(OperationalMetrics metrics) => _metrics = metrics;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            _metrics.ResetWindow();
        }
    }
}
