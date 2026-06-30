using AtoZClinical.Core.Entities;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Infrastructure.Services;

public enum OtpDeliveryMethod
{
    ServerLog,
    Email,
    Sms,
    WhatsApp
}

public static class OtpDeliveryConfiguration
{
    public const string DevelopmentLogMessage =
        "Your 4-digit code is in the server logs. Search for OTP LOG DELIVERY:";

    /// <summary>User-facing verification prompt (no SMTP/Twilio technical details).</summary>
    public static string BuildUserVerificationPrompt(
        OtpDeliveryMethod method,
        RegistrationVerificationChannel? channel = null,
        string? maskedDestination = null)
    {
        var effectiveChannel = ResolvePromptChannel(method, channel);
        var actuallyDelivered = method != OtpDeliveryMethod.ServerLog;

        return effectiveChannel switch
        {
            RegistrationVerificationChannel.Email => actuallyDelivered
                && !string.IsNullOrWhiteSpace(maskedDestination)
                ? $"Check your email for the verification code. We sent it to {maskedDestination}."
                : "Check your email for the verification code.",
            RegistrationVerificationChannel.Sms => actuallyDelivered
                && !string.IsNullOrWhiteSpace(maskedDestination)
                ? $"Check your phone for the verification code. We sent it to {maskedDestination}."
                : "Check your phone for the verification code.",
            RegistrationVerificationChannel.WhatsApp => actuallyDelivered
                && !string.IsNullOrWhiteSpace(maskedDestination)
                ? $"Check WhatsApp for the verification code. We sent it to {maskedDestination}."
                : "Check WhatsApp for the verification code.",
            _ => "Check your email for the verification code."
        };
    }

    public static string BuildSentMessage(
        OtpDeliveryMethod method,
        string? maskedDestination,
        RegistrationVerificationChannel? channel = null) =>
        BuildUserVerificationPrompt(method, channel, maskedDestination);

    public static bool ForceLogDelivery(IConfiguration? config) =>
        ReadBool(config, "Otp:ForceLogDelivery", "OTP_FORCE_LOG_DELIVERY");

    public static bool IsEmailAvailable(IConfiguration? config) =>
        SmtpEmailConfiguration.IsEmailConfigured(config);

    public static bool IsSmsAvailable(IConfiguration? config) =>
        SmsConfiguration.IsSmsConfigured(config);

    public static bool IsWhatsAppAvailable(IConfiguration? config) =>
        SmsConfiguration.IsWhatsAppConfigured(config);

    public static bool IsPhoneMessagingAvailable(IConfiguration? config) =>
        IsSmsAvailable(config) || IsWhatsAppAvailable(config);

    public static bool UsesServerLogFallback(IConfiguration? config) =>
        ForceLogDelivery(config)
        || (!IsEmailAvailable(config) && !IsPhoneMessagingAvailable(config));

    public static RegistrationVerificationChannel ResolveMobileChannel(
        IConfiguration? config,
        string? contactMethod)
    {
        var method = (contactMethod ?? "").Trim().ToLowerInvariant();
        if (method is "whatsapp" && IsWhatsAppAvailable(config))
            return RegistrationVerificationChannel.WhatsApp;
        if (method is "sms" && IsSmsAvailable(config))
            return RegistrationVerificationChannel.Sms;
        if (IsWhatsAppAvailable(config))
            return RegistrationVerificationChannel.WhatsApp;
        if (IsSmsAvailable(config))
            return RegistrationVerificationChannel.Sms;
        return RegistrationVerificationChannel.Sms;
    }

    public static string DescribeDeliveryMethod(OtpDeliveryMethod method) => method switch
    {
        OtpDeliveryMethod.Email => "email",
        OtpDeliveryMethod.Sms => "SMS",
        OtpDeliveryMethod.WhatsApp => "WhatsApp",
        _ => "email"
    };

    public static IReadOnlyDictionary<string, bool> GetDeliveryAvailability(IConfiguration? config) =>
        new Dictionary<string, bool>
        {
            ["email"] = IsEmailAvailable(config),
            ["sms"] = IsSmsAvailable(config),
            ["whatsapp"] = IsWhatsAppAvailable(config),
            ["serverLogFallback"] = UsesServerLogFallback(config),
            ["forceLogDelivery"] = ForceLogDelivery(config)
        };

    private static RegistrationVerificationChannel ResolvePromptChannel(
        OtpDeliveryMethod method,
        RegistrationVerificationChannel? channel) =>
        channel ?? method switch
        {
            OtpDeliveryMethod.Email => RegistrationVerificationChannel.Email,
            OtpDeliveryMethod.Sms => RegistrationVerificationChannel.Sms,
            OtpDeliveryMethod.WhatsApp => RegistrationVerificationChannel.WhatsApp,
            _ => RegistrationVerificationChannel.Email
        };

    private static bool ReadBool(IConfiguration? config, string configKey, string envKey)
    {
        var env = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(env) && bool.TryParse(env.Trim(), out var envValue))
            return envValue;

        return config?.GetValue(configKey, false) ?? false;
    }
}
