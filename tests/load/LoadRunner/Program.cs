using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;

var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://atoz-clinical.onrender.com";
var vus = int.Parse(args.ElementAtOrDefault(0) ?? "100");
var seconds = int.Parse(args.ElementAtOrDefault(1) ?? "30");

var endpoints = new[] { "/health", "/Account/Login", "/Portal/Login" };
var bag = new ConcurrentBag<(int Status, long Ms)>();
var end = DateTime.UtcNow.AddSeconds(seconds);
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var workers = Enumerable.Range(1, vus).Select(workerId => Task.Run(async () =>
{
    var rng = Random.Shared;
    while (DateTime.UtcNow < end)
    {
        var path = endpoints[rng.Next(endpoints.Length)];
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await client.GetAsync($"{baseUrl}{path}");
            sw.Stop();
            bag.Add(((int)response.StatusCode, sw.ElapsedMilliseconds));
        }
        catch
        {
            sw.Stop();
            bag.Add((0, sw.ElapsedMilliseconds));
        }

        await Task.Delay(50 + rng.Next(150));
    }
})).ToArray();

await Task.WhenAll(workers);

var results = bag.ToArray();
var durations = results.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
var errors = results.Count(r => r.Status >= 500 || r.Status == 0);
var p95Index = durations.Length == 0 ? 0 : Math.Min(durations.Length - 1, (int)Math.Ceiling(durations.Length * 0.95) - 1);

var summary = new
{
    scenario = $"dotnet-load-{vus}-vus",
    baseUrl,
    durationSeconds = seconds,
    totalRequests = results.Length,
    successCount = results.Length - errors,
    errorCount = errors,
    errorRate = results.Length == 0 ? 0 : Math.Round(errors / (double)results.Length, 4),
    avgMs = durations.Length == 0 ? 0 : Math.Round(durations.Average(), 1),
    p95Ms = durations.Length == 0 ? 0 : durations[p95Index],
    maxMs = durations.Length == 0 ? 0 : durations[^1]
};

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
