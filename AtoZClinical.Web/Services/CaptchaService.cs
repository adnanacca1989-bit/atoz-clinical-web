using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Web.Services;

public sealed class CaptchaService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CaptchaService> _logger;
    private readonly IHostEnvironment _env;

    public CaptchaService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<CaptchaService> logger,
        IHostEnvironment env)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _env = env;
    }

    public bool IsEnabled =>
        _config.GetValue("Captcha:Enabled", false) &&
        !string.IsNullOrWhiteSpace(_config["Captcha:SiteKey"]) &&
        !string.IsNullOrWhiteSpace(_config["Captcha:SecretKey"]);

    public string? SiteKey => _config["Captcha:SiteKey"];

    public async Task<bool> ValidateAsync(string? responseToken, string? remoteIp)
    {
        if (!IsEnabled)
            return _env.IsDevelopment();

        if (string.IsNullOrWhiteSpace(responseToken))
            return false;

        var secret = _config["Captcha:SecretKey"]!.Trim();
        var provider = (_config["Captcha:Provider"] ?? "hcaptcha").Trim().ToLowerInvariant();

        return provider switch
        {
            "recaptcha" or "google" => await ValidateRecaptchaAsync(secret, responseToken, remoteIp),
            _ => await ValidateHcaptchaAsync(secret, responseToken, remoteIp)
        };
    }

    private async Task<bool> ValidateHcaptchaAsync(string secret, string token, string? remoteIp)
    {
        var client = _httpClientFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token,
            ["remoteip"] = remoteIp ?? string.Empty
        });

        var response = await client.PostAsync("https://hcaptcha.com/siteverify", content);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("hCaptcha verify HTTP {Status}", response.StatusCode);
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<CaptchaVerifyResponse>();
        return payload?.Success == true;
    }

    private async Task<bool> ValidateRecaptchaAsync(string secret, string token, string? remoteIp)
    {
        var client = _httpClientFactory.CreateClient();
        var url =
            $"https://www.google.com/recaptcha/api/siteverify?secret={Uri.EscapeDataString(secret)}&response={Uri.EscapeDataString(token)}&remoteip={Uri.EscapeDataString(remoteIp ?? string.Empty)}";
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("reCAPTCHA verify HTTP {Status}", response.StatusCode);
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<CaptchaVerifyResponse>();
        return payload?.Success == true;
    }

    private sealed class CaptchaVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
