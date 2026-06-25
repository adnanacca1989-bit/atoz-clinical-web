namespace AtoZClinical.Web.Services;

/// <summary>In-memory request/error counters for operational health and alerting.</summary>
public sealed class OperationalMetrics
{
    private long _totalRequests;
    private long _serverErrors;
    private long _clientErrors;
    private DateTime _windowStartedUtc = DateTime.UtcNow;

    public void Record(int statusCode)
    {
        Interlocked.Increment(ref _totalRequests);
        if (statusCode >= 500)
            Interlocked.Increment(ref _serverErrors);
        else if (statusCode >= 400)
            Interlocked.Increment(ref _clientErrors);
    }

    public OperationalMetricsSnapshot GetSnapshot()
    {
        var total = Interlocked.Read(ref _totalRequests);
        var errors = Interlocked.Read(ref _serverErrors);
        var client = Interlocked.Read(ref _clientErrors);
        var errorRate = total > 0 ? Math.Round(errors * 100.0 / total, 2) : 0;

        return new OperationalMetricsSnapshot(
            total,
            errors,
            client,
            errorRate,
            _windowStartedUtc,
            GC.GetTotalMemory(false),
            Environment.WorkingSet);
    }

    public void ResetWindow()
    {
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _serverErrors, 0);
        Interlocked.Exchange(ref _clientErrors, 0);
        _windowStartedUtc = DateTime.UtcNow;
    }
}

public sealed record OperationalMetricsSnapshot(
    long TotalRequests,
    long ServerErrors,
    long ClientErrors,
    double ServerErrorRatePercent,
    DateTime WindowStartedUtc,
    long ManagedMemoryBytes,
    long WorkingSetBytes);
