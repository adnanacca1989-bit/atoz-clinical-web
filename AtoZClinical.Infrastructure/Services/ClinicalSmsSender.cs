using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public interface IClinicalSmsSender
{
    bool IsConfigured { get; }
    Task<ClinicalSmsSendResult> SendAsync(
        string toPhoneE164,
        string message,
        CancellationToken cancellationToken = default);
}

public sealed record ClinicalSmsSendResult(bool Success, string Message)
{
    public static ClinicalSmsSendResult Sent(string message = "SMS sent successfully") => new(true, message);
    public static ClinicalSmsSendResult Failed(string message) => new(false, message);
}

public sealed class TwilioClinicalSmsSender : IClinicalSmsSender
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TwilioClinicalSmsSender> _logger;

    public TwilioClinicalSmsSender(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<TwilioClinicalSmsSender> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsConfigured => SmsConfiguration.IsSmsConfigured(_config);

    public async Task<ClinicalSmsSendResult> SendAsync(
        string toPhoneE164,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toPhoneE164))
            throw new ArgumentException("Recipient phone is required.", nameof(toPhoneE164));

        if (!SmsConfiguration.IsSmsConfigured(_config))
        {
            var missing = SmsConfiguration.GetMissingVariables(_config);
            _logger.LogError(
                "SMS not sent to {To}: Twilio not configured. Missing: {Missing}",
                toPhoneE164, string.Join(", ", missing));
            throw new ClinicalSmsNotConfiguredException(missing);
        }

        var accountSid = SmsConfiguration.ReadAccountSid(_config)!;
        var authToken = SmsConfiguration.ReadAuthToken(_config)!;
        var from = SmsConfiguration.ReadFromNumber(_config)!;

        _logger.LogInformation("Sending SMS to {To} from {From}", toPhoneE164, from);

        var client = _httpClientFactory.CreateClient("TwilioSms");
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = from,
            ["To"] = toPhoneE164.Trim(),
            ["Body"] = message
        });

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Twilio SMS failed to {To}: HTTP {Status} {Body}",
                    toPhoneE164, (int)response.StatusCode, body);
                return ClinicalSmsSendResult.Failed("SMS could not be sent. Please try again later.");
            }

            _logger.LogInformation("SMS sent successfully to {To}", toPhoneE164);
            return ClinicalSmsSendResult.Sent();
        }
        catch (ClinicalSmsNotConfiguredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio SMS failed to {To}", toPhoneE164);
            return ClinicalSmsSendResult.Failed("SMS could not be sent. Please try again later.");
        }
    }
}
