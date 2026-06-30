using System.Security.Cryptography;
using System.Text;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AtoZClinical.Infrastructure.Services;

public sealed class TrialRegistrationVerificationService
{
    public const int CodeExpiryMinutes = 10;
    public const int MaxFailedAttempts = 5;
    public const int MaxCodesPerHour = 5;

    private readonly ClinicalDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClinicalEmailSender _email;
    private readonly IClinicalSmsSender _sms;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialRegistrationVerificationService> _logger;

    public TrialRegistrationVerificationService(
        ClinicalDbContext db,
        UserManager<ApplicationUser> users,
        IClinicalEmailSender email,
        IClinicalSmsSender sms,
        IConfiguration config,
        ILogger<TrialRegistrationVerificationService> logger)
    {
        _db = db;
        _users = users;
        _email = email;
        _sms = sms;
        _config = config;
        _logger = logger;
    }

    public async Task<VerificationCodeSendOutcome> SendCodeAsync(
        ApplicationUser user,
        RegistrationVerificationChannel channel,
        string destination,
        CancellationToken ct = default)
    {
        destination = NormalizeDestination(channel, destination);

        var hourAgo = DateTime.UtcNow.AddHours(-1);
        var recentCount = await _db.RegistrationVerificationCodes
            .CountAsync(c => c.UserId == user.Id && c.CreatedAt >= hourAgo, ct);
        if (recentCount >= MaxCodesPerHour)
            return VerificationCodeSendOutcome.RateLimited();

        await _db.RegistrationVerificationCodes
            .Where(c => c.UserId == user.Id && !c.Used && c.ExpiryDate > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Used, true), ct);

        var plainCode = GenerateFourDigitCode();
        _db.RegistrationVerificationCodes.Add(new RegistrationVerificationCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Channel = channel,
            Destination = destination,
            CodeHash = HashCode(plainCode),
            ExpiryDate = DateTime.UtcNow.AddMinutes(CodeExpiryMinutes),
            Used = false,
            FailedAttempts = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return await DeliverCodeAsync(user, plainCode, channel, destination, ct);
    }

    private async Task<VerificationCodeSendOutcome> DeliverCodeAsync(
        ApplicationUser user,
        string plainCode,
        RegistrationVerificationChannel channel,
        string destination,
        CancellationToken ct)
    {
        if (OtpDeliveryConfiguration.ForceLogDelivery(_config))
        {
            LogOtpForServerDelivery(user, plainCode, channel, destination);
            return BuildLogOutcome(channel, destination);
        }

        if (channel == RegistrationVerificationChannel.Email)
            return await DeliverEmailOtpAsync(user, plainCode, destination, ct);

        var smsText = $"Your A to Z Clinical verification code is {plainCode}. It expires in {CodeExpiryMinutes} minutes.";

        if (channel == RegistrationVerificationChannel.WhatsApp
            && OtpDeliveryConfiguration.IsWhatsAppAvailable(_config))
        {
            // WhatsApp channel: Twilio (TWILIO_WHATSAPP_FROM + TWILIO_ACCOUNT_SID + TWILIO_AUTH_TOKEN).
            var whatsAppResult = await _sms.SendWhatsAppAsync(destination, smsText, ct);
            if (!whatsAppResult.Success)
            {
                _logger.LogError("Verification WhatsApp failed for user {UserId}: {Reason}", user.Id, whatsAppResult.Message);
                return VerificationCodeSendOutcome.Failed(whatsAppResult.Message);
            }

            _logger.LogInformation("Verification code WhatsApp sent to {Destination} for user {UserId}", destination, user.Id);
            return VerificationCodeSendOutcome.Sent(
                channel,
                MaskPhone(destination),
                OtpDeliveryMethod.WhatsApp);
        }

        if (channel == RegistrationVerificationChannel.Sms
            && OtpDeliveryConfiguration.IsSmsAvailable(_config))
        {
            // SMS channel: Twilio (TWILIO_FROM_NUMBER + TWILIO_ACCOUNT_SID + TWILIO_AUTH_TOKEN).
            var smsResult = await _sms.SendSmsAsync(destination, smsText, ct);
            if (!smsResult.Success)
            {
                _logger.LogError("Verification SMS failed for user {UserId}: {Reason}", user.Id, smsResult.Message);
                return VerificationCodeSendOutcome.Failed(smsResult.Message);
            }

            _logger.LogInformation("Verification code SMS sent to {Destination} for user {UserId}", destination, user.Id);
            return VerificationCodeSendOutcome.Sent(
                channel,
                MaskPhone(destination),
                OtpDeliveryMethod.Sms);
        }

        LogOtpForServerDelivery(user, plainCode, channel, destination);
        return BuildLogOutcome(channel, destination);
    }

    /// <summary>
    /// Sends OTP by SMTP when all SMTP_* env vars are set; otherwise logs OTP LOG DELIVERY.
    /// </summary>
    private async Task<VerificationCodeSendOutcome> DeliverEmailOtpAsync(
        ApplicationUser user,
        string plainCode,
        string destination,
        CancellationToken ct)
    {
        if (!OtpDeliveryConfiguration.IsEmailConfigured(_config))
        {
            _logger.LogWarning(
                "SMTP not configured — OTP will be logged to server ({Label}). Set SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM on Render.",
                OtpLogDelivery.LogLabel);
            LogOtpForServerDelivery(user, plainCode, RegistrationVerificationChannel.Email, destination);
            return BuildLogOutcome(RegistrationVerificationChannel.Email, destination);
        }

        _logger.LogInformation(SmtpEmailConfiguration.OtpSendingLogMessage);

        var body = $"""
            <p>Hello {user.FullName},</p>
            <p>Your A to Z Clinical verification code is:</p>
            <p style="font-size:32px;font-weight:700;letter-spacing:8px;margin:24px 0">{plainCode}</p>
            <p style="color:#666;font-size:14px">This code expires in {CodeExpiryMinutes} minutes.</p>
            <p style="color:#666;font-size:14px">If you did not register, you can ignore this message.</p>
            """;

        ClinicalEmailSendResult result;
        try
        {
            result = await _email.SendAsync(
                destination,
                "Your A to Z Clinical verification code",
                body,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending OTP email for user {UserId}", user.Id);
            return VerificationCodeSendOutcome.Failed(SmtpEmailDiagnostics.ClassifyFailure(ex));
        }

        if (result.Skipped)
        {
            _logger.LogWarning("SMTP became unavailable — falling back to OTP LOG DELIVERY for user {UserId}", user.Id);
            LogOtpForServerDelivery(user, plainCode, RegistrationVerificationChannel.Email, destination);
            return BuildLogOutcome(RegistrationVerificationChannel.Email, destination);
        }

        if (!result.Success)
        {
            _logger.LogError("Verification email failed for user {UserId}: {Reason}", user.Id, result.Message);
            return VerificationCodeSendOutcome.Failed(result.Message);
        }

        _logger.LogInformation("OTP email sent to {Destination} for user {UserId}", destination, user.Id);
        return VerificationCodeSendOutcome.Sent(
            RegistrationVerificationChannel.Email,
            MaskEmail(destination),
            OtpDeliveryMethod.Email);
    }

    private static VerificationCodeSendOutcome BuildLogOutcome(
        RegistrationVerificationChannel channel,
        string destination)
    {
        var masked = channel == RegistrationVerificationChannel.Email
            ? MaskEmail(destination)
            : MaskPhone(destination);
        return VerificationCodeSendOutcome.SentViaLog(channel, masked);
    }

    public Task<VerificationCodeVerifyOutcome> VerifyCodeAsync(
        string userId,
        string code,
        CancellationToken ct = default) =>
        VerifyCodeCoreAsync(userId, null, code, ct);

    public async Task<VerificationCodeVerifyOutcome> VerifyCodeByUsernameAsync(
        string username,
        string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return VerificationCodeVerifyOutcome.UserNotFound();

        var normalized = username.Trim();
        var user = await _users.Users
            .FirstOrDefaultAsync(u => u.UserName == normalized || u.Email == normalized, ct);

        return user is null
            ? VerificationCodeVerifyOutcome.UserNotFound()
            : await VerifyCodeCoreAsync(user.Id, user, code, ct);
    }

    private async Task<VerificationCodeVerifyOutcome> VerifyCodeCoreAsync(
        string userId,
        ApplicationUser? user,
        string code,
        CancellationToken ct)
    {
        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 4)
            return VerificationCodeVerifyOutcome.InvalidCode();

        user ??= await _users.FindByIdAsync(userId);
        if (user is null)
            return VerificationCodeVerifyOutcome.UserNotFound();

        if (user.EmailConfirmed)
            return VerificationCodeVerifyOutcome.AlreadyVerified(user.Id);

        var hash = HashCode(digits);
        var row = await _db.RegistrationVerificationCodes
            .Where(c => c.UserId == user.Id && !c.Used && c.ExpiryDate > DateTime.UtcNow)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return VerificationCodeVerifyOutcome.Expired();

        if (row.CodeHash != hash)
        {
            row.FailedAttempts++;
            if (row.FailedAttempts >= MaxFailedAttempts)
                row.Used = true;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Invalid verification code for user {UserId} (attempt {Attempts})", user.Id, row.FailedAttempts);
            return VerificationCodeVerifyOutcome.InvalidCode();
        }

        row.Used = true;
        await _db.SaveChangesAsync(ct);

        user.EmailConfirmed = true;
        if (row.Channel is RegistrationVerificationChannel.Sms or RegistrationVerificationChannel.WhatsApp)
            user.PhoneNumberConfirmed = true;

        var update = await _users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            _logger.LogError(
                "Failed to confirm user {UserId} after valid code: {Errors}",
                user.Id, string.Join("; ", update.Errors.Select(e => e.Description)));
            return VerificationCodeVerifyOutcome.Failed("Account could not be confirmed. Please contact support.");
        }

        _logger.LogInformation("User {UserId} verified via {Channel}", user.Id, row.Channel);
        return VerificationCodeVerifyOutcome.Verified(user.Id);
    }

    private void LogOtpForServerDelivery(
        ApplicationUser user,
        string plainCode,
        RegistrationVerificationChannel channel,
        string destination)
    {
        if (!OtpDeliveryConfiguration.IsEmailAvailable(_config))
            SmtpEmailConfiguration.LogMissingVariablesAsErrors(_logger, _config);

        OtpLogDelivery.LogCode(
            _logger,
            plainCode,
            user.Id,
            user.UserName,
            channel.ToString(),
            destination,
            CodeExpiryMinutes);
    }

    public static string HashCode(string plainCode)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainCode.Trim()));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateFourDigitCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 10000);
        return value.ToString("D4");
    }

    private static string NormalizeDestination(RegistrationVerificationChannel channel, string destination)
    {
        if (channel == RegistrationVerificationChannel.Email)
            return destination.Trim();

        return PhoneNumberNormalizer.NormalizeOrThrow(destination);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***";
        return email[0] + "***" + email[at..];
    }

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 4) return "****";
        return "****" + phone[^4..];
    }
}

public sealed class VerificationCodeSendOutcome
{
    public VerificationCodeSendResult Result { get; init; }
    public string? MaskedDestination { get; init; }
    public RegistrationVerificationChannel? Channel { get; init; }
    public OtpDeliveryMethod DeliveryMethod { get; init; }
    public string? ErrorMessage { get; init; }
    public bool DeliveredViaLog => DeliveryMethod == OtpDeliveryMethod.ServerLog;

    public static VerificationCodeSendOutcome Sent(
        RegistrationVerificationChannel channel,
        string masked,
        OtpDeliveryMethod deliveryMethod) =>
        new()
        {
            Result = VerificationCodeSendResult.Sent,
            Channel = channel,
            MaskedDestination = masked,
            DeliveryMethod = deliveryMethod
        };

    public static VerificationCodeSendOutcome SentViaLog(RegistrationVerificationChannel channel, string masked) =>
        new()
        {
            Result = VerificationCodeSendResult.Sent,
            Channel = channel,
            MaskedDestination = masked,
            DeliveryMethod = OtpDeliveryMethod.ServerLog
        };

    public static VerificationCodeSendOutcome Failed(string? message) =>
        new() { Result = VerificationCodeSendResult.Failed, ErrorMessage = message };

    public static VerificationCodeSendOutcome RateLimited() =>
        new()
        {
            Result = VerificationCodeSendResult.RateLimited,
            ErrorMessage = "Too many codes requested. Please wait an hour and try again."
        };
}

public enum VerificationCodeSendResult
{
    Sent,
    Failed,
    RateLimited
}

public sealed class VerificationCodeVerifyOutcome
{
    public VerificationCodeVerifyResult Result { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UserId { get; init; }

    public static VerificationCodeVerifyOutcome Verified(string? userId = null) =>
        new() { Result = VerificationCodeVerifyResult.Verified, UserId = userId };

    public static VerificationCodeVerifyOutcome InvalidCode() =>
        new() { Result = VerificationCodeVerifyResult.InvalidCode, ErrorMessage = "Invalid verification code." };

    public static VerificationCodeVerifyOutcome Expired() =>
        new() { Result = VerificationCodeVerifyResult.Expired, ErrorMessage = "Code expired. Request a new one." };

    public static VerificationCodeVerifyOutcome AlreadyVerified(string? userId = null) =>
        new() { Result = VerificationCodeVerifyResult.AlreadyVerified, UserId = userId };

    public static VerificationCodeVerifyOutcome UserNotFound() =>
        new() { Result = VerificationCodeVerifyResult.UserNotFound, ErrorMessage = "Account not found." };

    public static VerificationCodeVerifyOutcome Failed(string message) =>
        new() { Result = VerificationCodeVerifyResult.Failed, ErrorMessage = message };
}

public enum VerificationCodeVerifyResult
{
    Verified,
    InvalidCode,
    Expired,
    AlreadyVerified,
    UserNotFound,
    Failed
}

public static class AccountVerificationPolicy
{
    public static bool IsVerificationConfigured(IConfiguration config) => true;

    public static bool CanVerifyViaEmail(IConfiguration config) => true;

    public static bool CanVerifyViaSms(IConfiguration config) => true;

    public static bool CanVerifyViaWhatsApp(IConfiguration config) => true;

    public static bool UsesLogOnlyDelivery(IConfiguration config) =>
        OtpDeliveryConfiguration.UsesServerLogFallback(config);
}
