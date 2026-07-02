using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Pages.Account;
using Microsoft.AspNetCore.RateLimiting;

namespace AtoZClinical.Web.Api;

public static class AuthOtpApiEndpoints
{
    public static void MapAuthOtpApi(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        var rateLimitOtp = !app.Environment.IsDevelopment();

        var sendOtp = auth.MapPost("/send-otp", async (
            SendOtpApiRequest body,
            ApplicationUserLookup userLookup,
            TrialRegistrationVerificationService verification,
            IConfiguration config,
            ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username))
                return Results.Json(new { success = false, error = "username is required" }, statusCode: StatusCodes.Status400BadRequest);

            var user = await userLookup.FindByUsernameOrEmailAsync(body.Username.Trim());
            if (user is null)
            {
                return Results.Json(new
                {
                    success = true,
                    message = "If an unconfirmed account exists, a verification code was generated."
                });
            }

            if (user.EmailConfirmed)
            {
                return Results.Json(new
                {
                    success = true,
                    message = "If an unconfirmed account exists, a verification code was generated."
                });
            }

            var (channel, destination) = VerifyAccountModel.ResolveChannel(user, config);
            if (string.IsNullOrWhiteSpace(destination))
            {
                return Results.Json(new
                {
                    success = false,
                    error = "Account has no email or mobile on file."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var outcome = await verification.SendCodeAsync(user, channel, destination);
                if (outcome.Result != VerificationCodeSendResult.Sent)
                {
                    return Results.Json(new
                    {
                        success = false,
                        error = outcome.ErrorMessage ?? "Could not generate verification code."
                    }, statusCode: StatusCodes.Status502BadGateway);
                }

                logger.LogInformation(
                    "OTP send API: userId={UserId} deliveredViaLog={LogDelivery}",
                    user.Id, outcome.DeliveredViaLog);

                return Results.Json(new
                {
                    success = true,
                    userId = user.Id,
                    deliveredViaLog = outcome.DeliveredViaLog,
                    deliveryMethod = OtpDeliveryConfiguration.DescribeDeliveryMethod(outcome.DeliveryMethod),
                    maskedDestination = outcome.MaskedDestination,
                    expiresInMinutes = TrialRegistrationVerificationService.CodeExpiryMinutes,
                    message = OtpDeliveryConfiguration.BuildUserVerificationPrompt(
                        config,
                        outcome.DeliveryMethod,
                        outcome.Channel,
                        outcome.MaskedDestination)
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "send-otp failed for {Username}", body.Username);
                return Results.Json(new { success = false, error = "Could not generate verification code." },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).AllowAnonymous();
        if (rateLimitOtp)
            sendOtp.RequireRateLimiting("auth-otp");

        var verifyOtp = auth.MapPost("/verify-otp", async (
            VerifyOtpApiRequest body,
            TrialRegistrationVerificationService verification,
            ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(body.Code))
                return Results.Json(new { success = false, error = "code is required" }, statusCode: StatusCodes.Status400BadRequest);

            VerificationCodeVerifyOutcome outcome;
            if (!string.IsNullOrWhiteSpace(body.UserId))
                outcome = await verification.VerifyCodeAsync(body.UserId.Trim(), body.Code);
            else if (!string.IsNullOrWhiteSpace(body.Username))
                outcome = await verification.VerifyCodeByUsernameAsync(body.Username.Trim(), body.Code);
            else
                return Results.Json(new { success = false, error = "userId or username is required" },
                    statusCode: StatusCodes.Status400BadRequest);

            return outcome.Result switch
            {
                VerificationCodeVerifyResult.Verified => Results.Json(new
                {
                    success = true,
                    verified = true,
                    userId = outcome.UserId,
                    message = "Account activated. You can sign in now."
                }),
                VerificationCodeVerifyResult.AlreadyVerified => Results.Json(new
                {
                    success = true,
                    verified = true,
                    alreadyVerified = true,
                    userId = outcome.UserId,
                    message = "Account is already verified."
                }),
                VerificationCodeVerifyResult.InvalidCode => Results.Json(new
                {
                    success = false,
                    error = outcome.ErrorMessage
                }, statusCode: StatusCodes.Status400BadRequest),
                VerificationCodeVerifyResult.Expired => Results.Json(new
                {
                    success = false,
                    error = outcome.ErrorMessage
                }, statusCode: StatusCodes.Status410Gone),
                VerificationCodeVerifyResult.UserNotFound => Results.Json(new
                {
                    success = false,
                    error = outcome.ErrorMessage
                }, statusCode: StatusCodes.Status404NotFound),
                _ => Results.Json(new
                {
                    success = false,
                    error = outcome.ErrorMessage ?? "Verification failed."
                }, statusCode: StatusCodes.Status500InternalServerError)
            };
        }).AllowAnonymous();
        if (rateLimitOtp)
            verifyOtp.RequireRateLimiting("auth-otp");
    }
}

public sealed record SendOtpApiRequest(string Username);

public sealed record VerifyOtpApiRequest(string? UserId, string? Username, string Code);
