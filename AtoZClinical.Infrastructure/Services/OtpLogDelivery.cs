using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

/// <summary>
/// OTP delivery channels. Email uses MailKit SMTP (ASP.NET equivalent of nodemailer).
/// SMS and WhatsApp use Twilio when TWILIO_* environment variables are set.
/// When SMTP is not configured, codes are written to logs with <see cref="LogLabel"/>.
/// </summary>
public static class OtpLogDelivery
{
    public const string LogLabel = "OTP LOG DELIVERY:";

    public static void LogCode(
        ILogger logger,
        string plainCode,
        string userId,
        string? username,
        string channel,
        string destination,
        int expiryMinutes)
    {
        // Primary line for Render log search: OTP LOG DELIVERY: 1234
        logger.LogWarning("{Label} {Otp}", LogLabel, plainCode);

        logger.LogWarning(
            "{Label} {Otp} | UserId={UserId} Username={Username} Channel={Channel} Destination={Destination} ExpiresInMinutes={Expiry} | Configure SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM on Render for email delivery.",
            LogLabel,
            plainCode,
            userId,
            username,
            channel,
            destination,
            expiryMinutes);
    }
}
