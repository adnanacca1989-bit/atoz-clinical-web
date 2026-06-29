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

        if (channel == RegistrationVerificationChannel.Email)
        {
            if (!SmtpEmailConfiguration.IsEmailConfigured(_config))
            {
                var missing = SmtpEmailConfiguration.GetMissingVariables(_config);
                throw new ClinicalEmailNotConfiguredException(missing);
            }
        }
        else
        {
            if (!SmsConfiguration.IsSmsConfigured(_config))
            {
                var missing = SmsConfiguration.GetMissingVariables(_config);
                throw new ClinicalSmsNotConfiguredException(missing);
            }
        }

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

        if (channel == RegistrationVerificationChannel.Email)
        {
            var body = $"""
                <p>Hello {user.FullName},</p>
                <p>Your A to Z Clinical verification code is:</p>
                <p style="font-size:32px;font-weight:700;letter-spacing:8px;margin:24px 0">{plainCode}</p>
                <p style="color:#666;font-size:14px">This code expires in {CodeExpiryMinutes} minutes.</p>
                <p style="color:#666;font-size:14px">If you did not register, you can ignore this message.</p>
                """;

            var result = await _email.SendAsync(destination, "Your A to Z Clinical verification code", body, ct);
            if (!result.Success)
            {
                _logger.LogError("Verification email failed for user {UserId}: {Reason}", user.Id, result.Message);
                return VerificationCodeSendOutcome.Failed(result.Message);
            }

            _logger.LogInformation("Verification code email sent to {Destination} for user {UserId}", destination, user.Id);
            return VerificationCodeSendOutcome.Sent(channel, MaskEmail(destination));
        }

        var smsText = $"Your A to Z Clinical verification code is {plainCode}. It expires in {CodeExpiryMinutes} minutes.";
        var smsResult = await _sms.SendAsync(destination, smsText, ct);
        if (!smsResult.Success)
        {
            _logger.LogError("Verification SMS failed for user {UserId}: {Reason}", user.Id, smsResult.Message);
            return VerificationCodeSendOutcome.Failed(smsResult.Message);
        }

        _logger.LogInformation("Verification code SMS sent to {Destination} for user {UserId}", destination, user.Id);
        return VerificationCodeSendOutcome.Sent(channel, MaskPhone(destination));
    }

    public async Task<VerificationCodeVerifyOutcome> VerifyCodeAsync(
        string userId,
        string code,
        CancellationToken ct = default)
    {
        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 4)
            return VerificationCodeVerifyOutcome.InvalidCode();

        var user = await _users.FindByIdAsync(userId);
        if (user is null)
            return VerificationCodeVerifyOutcome.UserNotFound();

        if (user.EmailConfirmed)
            return VerificationCodeVerifyOutcome.AlreadyVerified();

        var hash = HashCode(digits);
        var row = await _db.RegistrationVerificationCodes
            .Where(c => c.UserId == userId && !c.Used && c.ExpiryDate > DateTime.UtcNow)
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
            _logger.LogWarning("Invalid verification code for user {UserId} (attempt {Attempts})", userId, row.FailedAttempts);
            return VerificationCodeVerifyOutcome.InvalidCode();
        }

        row.Used = true;
        await _db.SaveChangesAsync(ct);

        user.EmailConfirmed = true;
        if (row.Channel == RegistrationVerificationChannel.Sms)
            user.PhoneNumberConfirmed = true;

        var update = await _users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            _logger.LogError(
                "Failed to confirm user {UserId} after valid code: {Errors}",
                userId, string.Join("; ", update.Errors.Select(e => e.Description)));
            return VerificationCodeVerifyOutcome.Failed("Account could not be confirmed. Please contact support.");
        }

        _logger.LogInformation("User {UserId} verified via {Channel}", userId, row.Channel);
        return VerificationCodeVerifyOutcome.Verified();
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
    public string? ErrorMessage { get; init; }

    public static VerificationCodeSendOutcome Sent(RegistrationVerificationChannel channel, string masked) =>
        new()
        {
            Result = VerificationCodeSendResult.Sent,
            Channel = channel,
            MaskedDestination = masked
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

    public static VerificationCodeVerifyOutcome Verified() =>
        new() { Result = VerificationCodeVerifyResult.Verified };

    public static VerificationCodeVerifyOutcome InvalidCode() =>
        new() { Result = VerificationCodeVerifyResult.InvalidCode, ErrorMessage = "Invalid verification code." };

    public static VerificationCodeVerifyOutcome Expired() =>
        new() { Result = VerificationCodeVerifyResult.Expired, ErrorMessage = "Code expired. Request a new one." };

    public static VerificationCodeVerifyOutcome AlreadyVerified() =>
        new() { Result = VerificationCodeVerifyResult.AlreadyVerified };

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
    public static bool IsVerificationConfigured(IConfiguration config) =>
        SmtpEmailConfiguration.IsEmailConfigured(config) || SmsConfiguration.IsSmsConfigured(config);

    public static bool CanVerifyViaEmail(IConfiguration config) =>
        SmtpEmailConfiguration.IsEmailConfigured(config);

    public static bool CanVerifyViaSms(IConfiguration config) =>
        SmsConfiguration.IsSmsConfigured(config);
}
