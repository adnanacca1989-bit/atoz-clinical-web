using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public interface IClinicalSmsSender
{
    bool IsSmsConfigured { get; }
    bool IsWhatsAppConfigured { get; }
    Task<ClinicalSmsSendResult> SendSmsAsync(
        string toPhoneE164,
        string message,
        CancellationToken cancellationToken = default);
    Task<ClinicalSmsSendResult> SendWhatsAppAsync(
        string toPhoneE164,
        string message,
        CancellationToken cancellationToken = default);
}

public sealed record ClinicalSmsSendResult(bool Success, string Message)
{
    public static ClinicalSmsSendResult Sent(string message = "Message sent successfully") => new(true, message);
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

    public bool IsSmsConfigured => SmsConfiguration.IsSmsConfigured(_config);
    public bool IsWhatsAppConfigured => SmsConfiguration.IsWhatsAppConfigured(_config);

    public Task<ClinicalSmsSendResult> SendSmsAsync(
        string toPhoneE164,
        string message,
        CancellationToken cancellationToken = default) =>
        SendTwilioMessageAsync(
            toPhoneE164,
            message,
            SmsConfiguration.ReadSmsFromNumber(_config)!,
            toPhoneE164.Trim(),
            "SMS",
            SmsConfiguration.GetSmsMissingVariables(_config),
            cancellationToken);

    public Task<ClinicalSmsSendResult> SendWhatsAppAsync(
        string toPhoneE164,
        string message,
        CancellationToken cancellationToken = default) =>
        SendTwilioMessageAsync(
            toPhoneE164,
            message,
            SmsConfiguration.ReadWhatsAppFromNumber(_config)!,
            SmsConfiguration.FormatWhatsAppAddress(toPhoneE164),
            "WhatsApp",
            SmsConfiguration.GetWhatsAppMissingVariables(_config),
            cancellationToken);

    private async Task<ClinicalSmsSendResult> SendTwilioMessageAsync(
        string toPhoneE164,
        string message,
        string from,
        string to,
        string channelLabel,
        IReadOnlyList<string> missingWhenNotConfigured,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toPhoneE164))
            throw new ArgumentException("Recipient phone is required.", nameof(toPhoneE164));

        if (!SmsConfiguration.IsTwilioCoreConfigured(_config) || string.IsNullOrWhiteSpace(from))
        {
            _logger.LogError(
                "{Channel} not sent to {To}: Twilio not configured. Missing: {Missing}",
                channelLabel, toPhoneE164, string.Join(", ", missingWhenNotConfigured));
            throw new ClinicalSmsNotConfiguredException(missingWhenNotConfigured);
        }

        var accountSid = SmsConfiguration.ReadAccountSid(_config)!;
        var authToken = SmsConfiguration.ReadAuthToken(_config)!;

        _logger.LogInformation("Sending {Channel} to {To} from {From}", channelLabel, to, from);

        var client = _httpClientFactory.CreateClient("TwilioSms");
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = from,
            ["To"] = to,
            ["Body"] = message
        });

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Twilio {Channel} failed to {To}: HTTP {Status} {Body}",
                    channelLabel, toPhoneE164, (int)response.StatusCode, body);
                return ClinicalSmsSendResult.Failed($"{channelLabel} could not be sent. Please try again later.");
            }

            _logger.LogInformation("{Channel} sent successfully to {To}", channelLabel, toPhoneE164);
            return ClinicalSmsSendResult.Sent();
        }
        catch (ClinicalSmsNotConfiguredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio {Channel} failed to {To}", channelLabel, toPhoneE164);
            return ClinicalSmsSendResult.Failed($"{channelLabel} could not be sent. Please try again later.");
        }
    }
}
