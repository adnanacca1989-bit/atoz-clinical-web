using AtoZClinical.Web;
using AtoZClinical.Web.DataProtection;
using AtoZClinical.Web.Filters;
using AtoZClinical.Web.Hubs;
using AtoZClinical.Web.Api;
using AtoZClinical.Web.Middleware;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using System.Text.Json;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

SmtpConfigurationBootstrap.Apply(builder.Configuration);

{
    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    SmtpEmailConfiguration.LogEnvironmentPresence(loggerFactory.CreateLogger("Startup"));
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "AtoZClinical")
    .WriteTo.Console());

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
    }
    return null;
}

static string NormalizeConnectionString(string value)
{
    if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        value = "postgresql://" + value["postgres://".Length..];

    if (!value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return value;

    var uri = new Uri(value);
    var userInfo = uri.UserInfo.Split(':', 2);
    var csb = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(userInfo[0]),
        SslMode = SslMode.Require
    };
    if (userInfo.Length > 1)
        csb.Password = Uri.UnescapeDataString(userInfo[1]);

    return csb.ConnectionString;
}

static string WithPoolSettings(string value)
{
    if (value.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
        return value;
    return value.TrimEnd(';') + ";Maximum Pool Size=20;Minimum Pool Size=2;Connection Idle Lifetime=60";
}

var rawConnectionString = FirstNonEmpty(
    builder.Configuration.GetConnectionString("ClinicalDatabase"),
    builder.Configuration["DATABASE_URL"],
    Environment.GetEnvironmentVariable("ConnectionStrings__ClinicalDatabase"),
    Environment.GetEnvironmentVariable("DATABASE_URL"),
    Environment.GetEnvironmentVariable("RENDER_DATABASE_URL"));

if (string.IsNullOrWhiteSpace(rawConnectionString))
    throw new InvalidOperationException(
        "Database connection string is missing. Set ConnectionStrings__ClinicalDatabase or DATABASE_URL on Render.");

var normalized = NormalizeConnectionString(rawConnectionString);
var useSqlite = builder.Configuration.GetValue("Database:Provider", "PostgreSQL") == "Sqlite"
    || normalized.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);
var connectionString = useSqlite ? normalized : WithPoolSettings(normalized);

builder.Services.AddDbContext<ClinicalDbContext>(options =>
{
    if (useSqlite)
        options.UseSqlite(connectionString);
    else
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(ClinicalDbContext).Assembly.FullName));
});

builder.Services.AddClinicalDataProtection(builder.Configuration, connectionString, useSqlite);

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__ClinicalAntiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.HeaderName = "X-CSRF-TOKEN";
});

var smtpConfiguredAtStartup = SmtpEmailConfiguration.IsEmailConfigured(builder.Configuration);
var smsConfiguredAtStartup = SmsConfiguration.IsSmsConfigured(builder.Configuration);
var accountVerificationRequiredAtStartup = AccountVerificationPolicy.IsRequired(builder.Configuration);

builder.Services.AddHttpClient("TwilioSms");

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 12;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<ClinicalDbContext>()
    .AddDefaultTokenProviders();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var msClientId = builder.Configuration["Authentication:Microsoft:ClientId"];
var msSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"];

var externalAuth = builder.Services.AddAuthentication();
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleSecret))
{
    externalAuth.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleSecret;
    });
}
if (!string.IsNullOrWhiteSpace(msClientId) && !string.IsNullOrWhiteSpace(msSecret))
{
    externalAuth.AddMicrosoftAccount(options =>
    {
        options.ClientId = msClientId;
        options.ClientSecret = msSecret;
    });
}

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(24);
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

    options.Events = new CookieAuthenticationEvents
    {
        OnSigningIn = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AuthCookie");
            logger.LogInformation(
                "SignIn cookie being issued for {User} scheme={Scheme} persistent={Persistent} expires={Expires} trace={TraceId}",
                context.Principal?.Identity?.Name ?? "(unknown)",
                context.Scheme.Name,
                context.Properties.IsPersistent,
                context.Properties.ExpiresUtc?.ToString("O") ?? "(session)",
                context.HttpContext.TraceIdentifier);
            return Task.CompletedTask;
        },
        OnSignedIn = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AuthCookie");
            logger.LogInformation(
                "SignIn cookie issued for {User} scheme={Scheme} trace={TraceId}",
                context.Principal?.Identity?.Name ?? "(unknown)",
                context.Scheme.Name,
                context.HttpContext.TraceIdentifier);
            return Task.CompletedTask;
        },
        OnValidatePrincipal = context =>
        {
            if (context.Principal?.Identity?.IsAuthenticated != true)
                return Task.CompletedTask;

            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase))
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthCookie");
                logger.LogDebug(
                    "Existing auth cookie validated on login path for {User} trace={TraceId}",
                    context.Principal.Identity?.Name,
                    context.HttpContext.TraceIdentifier);
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("register", limiter =>
    {
        limiter.Window = TimeSpan.FromHours(1);
        limiter.PermitLimit = 8;
        limiter.QueueLimit = 0;
    });
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 52_428_800;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ClinicalRoles.Vendor, policy => policy.RequireRole(ClinicalRoles.Vendor));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<OperationalMetrics>();
builder.Services.AddHttpClient("operations-alerts");
builder.Services.AddHostedService<OperationalMetricsResetService>();
builder.Services.AddScoped<ICurrentClinicProvider, HttpContextClinicProvider>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<ApplicationUserLookup>();
builder.Services.AddScoped<IClinicalEmailSender, SmtpClinicalEmailSender>();
builder.Services.AddScoped<IClinicalSmsSender, TwilioClinicalSmsSender>();
builder.Services.AddScoped<TrialRegistrationVerificationService>();
builder.Services.AddScoped<RegistrationEmailService>();
builder.Services.AddScoped<SubscriptionEmailService>();
builder.Services.AddScoped<VendorAnalyticsService>();
builder.Services.AddScoped<SaasSubscriptionService>();
builder.Services.AddScoped<SecurityAuditService>();
builder.Services.AddScoped<ClinicProfileService>();
builder.Services.AddScoped<ClinicBackupHistoryService>();
builder.Services.AddScoped<ClinicDataDeletionService>();
if (builder.Configuration.GetValue("Billing:Enabled", false) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["Stripe:SecretKey"]))
{
    builder.Services.AddScoped<IStripeBillingService, StripeBillingService>();
}
else
{
    builder.Services.AddScoped<IStripeBillingService, NoOpStripeBillingService>();
}
builder.Services.AddScoped<ClinicalAppUrls>();
builder.Services.AddScoped<CaptchaService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ClinicContextService>();
builder.Services.AddScoped<ClinicAccessService>();
builder.Services.AddScoped<VendorClinicService>();
builder.Services.AddScoped<MasterDataPropagationService>();
builder.Services.AddScoped<PatientService>();
builder.Services.AddScoped<AppointmentService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.AddScoped<ServiceIncomeService>();
builder.Services.AddScoped<ServiceIncomeRequestService>();
builder.Services.AddScoped<CashReceiptService>();
builder.Services.AddScoped<LabTestService>();
builder.Services.AddScoped<LabRequestService>();
builder.Services.AddScoped<LabResultService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<RadiologyTestService>();
builder.Services.AddScoped<RadiologyRequestService>();
builder.Services.AddScoped<RadiologyResultService>();
builder.Services.AddScoped<PrescriptionService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<CashPaymentService>();
builder.Services.AddScoped<ChartAccountService>();
builder.Services.AddScoped<RolePermissionService>();
builder.Services.AddScoped<PharmacyRequestService>();
builder.Services.AddScoped<PharmacyBillService>();
builder.Services.AddScoped<PharmacyInventoryService>();
builder.Services.AddScoped<PharmacyOpeningBalanceService>();
builder.Services.AddScoped<PharmacyItemRegistrationService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ClinicRuntimeCache>();
builder.Services.AddScoped<ClinicSettingsService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ClinicLookupService>();
builder.Services.AddScoped<PharmacyPurchaseBillService>();
builder.Services.AddScoped<ArReportService>();
builder.Services.AddScoped<PatientInvoiceService>();
builder.Services.AddScoped<ClinicalDemographicsSyncService>();
builder.Services.AddScoped<BillingPropagationService>();
builder.Services.AddScoped<PatientVisitHistoryService>();
builder.Services.AddScoped<InvoiceDeleteGuardService>();
builder.Services.AddScoped<PatientVisitStatusService>();
builder.Services.AddScoped<FormPermissionService>();
builder.Services.AddScoped<ClinicModuleService>();
builder.Services.AddScoped<DoctorReportService>();
builder.Services.AddScoped<RequestReportService>();
builder.Services.AddScoped<GlobalTransactionSearchService>();
builder.Services.AddScoped<FormPermissionPageFilter>();
builder.Services.AddScoped<ReportBrandingPageFilter>();
builder.Services.AddScoped<ClinicBrandingPageFilter>();
builder.Services.AddScoped<PharmacyCogsService>();
builder.Services.AddScoped<ExpenseVoucherService>();
builder.Services.AddScoped<ClinicalJournalSyncService>();
builder.Services.AddScoped<JournalReportService>();
builder.Services.AddScoped<FinancialReportCalculator>();
builder.Services.AddScoped<AppointmentReminderService>();
builder.Services.AddScoped<PatientPrintBundleService>();
builder.Services.AddScoped<ClinicBackupService>();
builder.Services.AddScoped<MfaPolicyService>();
builder.Services.AddScoped<ClinicApiKeyService>();
builder.Services.AddScoped<WebhookSubscriptionService>();
builder.Services.AddScoped<IWebhookDispatchService, WebhookDispatchService>();
builder.Services.AddScoped<SubdomainClinicResolver>();
builder.Services.AddScoped<PatientPortalService>();
builder.Services.AddScoped<ReportingDataService>();
builder.Services.AddScoped<PatientPortalSession>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, SignalRUserIdProvider>();
builder.Services.AddSingleton<ChatPresenceService>();
builder.Services.AddScoped<ClinicMessagingService>();
builder.Services.AddScoped<DoctorScopeContext>();
builder.Services.AddScoped<DoctorScopeService>();
builder.Services.AddScoped<DoctorUserLinkService>();
builder.Services.AddScoped<ClinicalNotificationService>();
builder.Services.AddScoped<IClinicalNotificationPublisher, SignalRClinicalNotificationPublisher>();
builder.Services.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<ClinicLicenseMaintenanceService>();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddFolderApplicationModelConvention("/", model =>
    {
        model.Filters.Add(new ServiceFilterAttribute(typeof(FormPermissionPageFilter)));
        model.Filters.Add(new ServiceFilterAttribute(typeof(ClinicBrandingPageFilter)));
    });
    options.Conventions.AddFolderApplicationModelConvention("/Reports", model =>
    {
        model.Filters.Add(new ServiceFilterAttribute(typeof(ReportBrandingPageFilter)));
    });
    options.Conventions.AddFolderApplicationModelConvention("/Account", model =>
    {
        model.Filters.Add(new IgnoreAntiforgeryTokenAttribute());
    });
    options.Conventions.AddPageApplicationModelConvention("/Error", model =>
    {
        model.Filters.Add(new IgnoreAntiforgeryTokenAttribute());
    });
    options.Conventions.AuthorizeFolder("/Vendor", ClinicalRoles.Vendor);
    options.Conventions.AuthorizeFolder("/Dashboard");
    options.Conventions.AuthorizeFolder("/Doctors");
    options.Conventions.AuthorizeFolder("/ServiceIncomes");
    options.Conventions.AuthorizeFolder("/PatientRegistration");
    options.Conventions.AuthorizeFolder("/CashReceipts");
    options.Conventions.AuthorizeFolder("/CashPayments");
    options.Conventions.AuthorizeFolder("/Laboratory");
    options.Conventions.AuthorizeFolder("/Radiology");
    options.Conventions.AuthorizeFolder("/Prescriptions");
    options.Conventions.AuthorizeFolder("/Invoices");
    options.Conventions.AuthorizeFolder("/ChartOfAccounts");
    options.Conventions.AuthorizeFolder("/Reports");
    options.Conventions.AuthorizeFolder("/Admin");
    options.Conventions.AuthorizeFolder("/Billing");
    options.Conventions.AuthorizeFolder("/Pharmacy");
    options.Conventions.AuthorizeFolder("/Settings");
    options.Conventions.AuthorizeFolder("/Notifications");
    options.Conventions.AuthorizeFolder("/Messages");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Account/LicenseBlocked");
    options.Conventions.AllowAnonymousToPage("/Account/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/Account/ResetPassword");
    options.Conventions.AllowAnonymousToPage("/Account/ConfirmEmail");
    options.Conventions.AllowAnonymousToPage("/Account/ResendConfirmation");
    options.Conventions.AllowAnonymousToPage("/Account/VerifyAccount");
    options.Conventions.AllowAnonymousToPage("/Register/Clinic");
    options.Conventions.AllowAnonymousToPage("/Register/Trial");
    options.Conventions.AllowAnonymousToPage("/Legal/Terms");
    options.Conventions.AllowAnonymousToPage("/Legal/Privacy");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToFolder("/Portal");
    options.Conventions.AllowAnonymousToPage("/Account/LoginWith2fa");
    options.Conventions.AllowAnonymousToPage("/Account/ExternalLogin");
});

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

var emailSettings = SmtpEmailSettings.From(app.Configuration);
SmtpEmailConfiguration.LogDiagnostics(app.Logger, app.Configuration);
if (smtpConfiguredAtStartup)
{
    app.Logger.LogInformation(SmtpEmailConfiguration.StartupSuccessMessage);
    app.Logger.LogInformation("Email confirmation is enabled (SMTP configured).");
}
else
    app.Logger.LogWarning("{SmtpError}", SmtpEmailConfiguration.FormatMissingConfigurationError(app.Configuration));

SmsConfiguration.LogDiagnostics(app.Logger, app.Configuration);
if (smsConfiguredAtStartup)
    app.Logger.LogInformation("SMS verification is enabled (Twilio configured).");
else if (accountVerificationRequiredAtStartup)
    app.Logger.LogWarning("Twilio SMS is not configured — mobile verification codes cannot be sent.");

if (!accountVerificationRequiredAtStartup)
    app.Logger.LogWarning(
        "Account verification is disabled. Users can sign in without a verification code.");
else if (AccountVerificationPolicy.UsesLogOnlyDelivery(app.Configuration))
    app.Logger.LogWarning(
        "OTP uses server log delivery (development). Configure SMTP for email and/or Twilio for SMS/WhatsApp.");
else
{
    if (OtpDeliveryConfiguration.IsEmailAvailable(app.Configuration))
        app.Logger.LogInformation("OTP email delivery enabled (SMTP).");
    if (OtpDeliveryConfiguration.IsSmsAvailable(app.Configuration))
        app.Logger.LogInformation("OTP SMS delivery enabled (Twilio).");
    if (OtpDeliveryConfiguration.IsWhatsAppAvailable(app.Configuration))
        app.Logger.LogInformation("OTP WhatsApp delivery enabled (Twilio).");
}
if (!string.IsNullOrWhiteSpace(emailSettings.ConfigurationWarning))
    app.Logger.LogWarning(emailSettings.ConfigurationWarning);
await ClinicalDataProtectionSetup.WarmUpAsync(app.Services, useSqlite, app.Logger);

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        ExceptionHandlingPath = "/Error",
        AllowStatusCode404Response = true
    });
    app.UseHsts();
}
app.UseStaticFiles();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<LanguageMiddleware>();
app.UseRouting();
app.UseMiddleware<LoginStabilizationMiddleware>();
app.UseMiddleware<AuthPostRateLimitMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<LoginCookieSanitizerMiddleware>();
app.UseMiddleware<DataProtectionRecoveryMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ClinicSubdomainMiddleware>();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<ClinicTenantMiddleware>();
app.UseMiddleware<DoctorScopeMiddleware>();
app.UseMiddleware<FormPermissionMiddleware>();
app.UseAuthorization();
app.UseMiddleware<MfaEnforcementMiddleware>();
app.UseMiddleware<PhiAccessAuditMiddleware>();
app.UseMiddleware<OperationsMonitoringMiddleware>();
app.UseMiddleware<LoginAuditMiddleware>();
app.UseMiddleware<LoginAuthDiagnosticsMiddleware>();
app.MapGet("/health", async (HttpContext ctx, ClinicalDbContext db, OperationalMetrics metrics, IConfiguration config, DataProtectionDbContext dpDb) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync())
            return Results.Problem("Database unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);

        var emailConfigured = SmtpEmailConfiguration.IsEmailConfigured(config);
        var basic = new Dictionary<string, object?>
        {
            ["status"] = "healthy",
            ["version"] = AppBuildInfo.Version,
            ["timestamp"] = DateTime.UtcNow,
            ["isHttps"] = ctx.Request.IsHttps,
            ["emailConfigured"] = emailConfigured,
            ["smsConfigured"] = SmsConfiguration.IsSmsConfigured(config),
            ["whatsappConfigured"] = SmsConfiguration.IsWhatsAppConfigured(config),
            ["otpDelivery"] = OtpDeliveryConfiguration.GetDeliveryAvailability(config),
            ["otpLogDelivery"] = OtpDeliveryConfiguration.UsesServerLogFallback(config),
            ["emailConfirmationRequired"] = AccountVerificationPolicy.IsRequired(config),
            ["emailStatus"] = emailConfigured ? "ready" : SmtpEmailSettings.From(config).DescribeReadiness(),
            ["emailConfigurationError"] = emailConfigured ? null : SmtpEmailConfiguration.FormatMissingConfigurationError(config),
            ["emailMissingVariables"] = SmtpEmailConfiguration.GetMissingVariables(config),
            ["smtpEnvUnset"] = SmtpEmailConfiguration.GetUnsetProcessEnvironmentVariables(),
            ["smtpVariables"] = SmtpEmailConfiguration.GetVariablePresence(config)
        };

        var token = config["Operations:HealthToken"];
        if (!string.IsNullOrWhiteSpace(token) &&
            ctx.Request.Headers.TryGetValue("X-Health-Token", out var provided) &&
            provided == token)
        {
            basic["database"] = "connected";
            basic["metrics"] = metrics.GetSnapshot();
            try
            {
                basic["dataProtectionKeys"] = await dpDb.DataProtectionKeys.CountAsync();
            }
            catch (Exception ex)
            {
                basic["dataProtectionKeys"] = $"error: {ex.Message}";
            }
        }

        return Results.Content(JsonSerializer.Serialize(basic), "application/json");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Health check failed");
        return Results.Problem("Health check failed", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous().DisableRateLimiting();

app.MapGet("/health/email", (IConfiguration config) =>
    Results.Content(
        JsonSerializer.Serialize(SmtpEmailConfiguration.BuildEmailHealthPayload(config)),
        "application/json"))
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapGet("/debug-email-config", (IConfiguration config) =>
    Results.Json(SmtpEmailConfiguration.GetVariablePresence(config)))
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapGet("/test-email", async (HttpContext ctx, IClinicalEmailSender email, IConfiguration config, ILogger<Program> logger) =>
{
    var healthToken = config["Operations:HealthToken"];
    var isDev = app.Environment.IsDevelopment();
    if (!isDev)
    {
        if (string.IsNullOrWhiteSpace(healthToken)
            || !ctx.Request.Headers.TryGetValue("X-Health-Token", out var provided)
            || provided != healthToken)
        {
            return Results.Json(new { success = false, error = "Unauthorized. Send header X-Health-Token." }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    var to = ctx.Request.Query["to"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(to) || !to.Contains('@'))
        return Results.Json(new { success = false, error = "Provide ?to=email@example.com" }, statusCode: StatusCodes.Status400BadRequest);

    try
    {
        var sendResult = await email.SendAsync(to,
            "A to Z Clinical — SMTP test",
            "<p>This is a test email from A to Z Clinical. SMTP is working.</p>");

        if (sendResult.Skipped)
        {
            var missing = SmtpEmailConfiguration.GetMissingVariables(config);
            return Results.Json(new
            {
                success = false,
                error = SmtpEmailConfiguration.FormatMissingConfigurationError(config),
                missing
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!sendResult.Success)
        {
            logger.LogError("Test email failed to {To}: {Reason}", to, sendResult.Message);
            return Results.Json(new
            {
                success = false,
                error = sendResult.Message,
                userMessage = SmtpEmailDiagnostics.UserFriendlyFailureMessage
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Json(new
        {
            success = true,
            to,
            host = SmtpEmailSettings.From(config).Host,
            port = SmtpEmailSettings.From(config).Port,
            from = SmtpEmailSettings.From(config).FromAddress,
            message = sendResult.Message
        });
    }
    catch (ClinicalEmailNotConfiguredException ex)
    {
        logger.LogError(ex, "Test email blocked: SMTP not configured");
        return Results.Json(new
        {
            success = false,
            configured = false,
            error = ex.Message,
            missing = ex.MissingVariables,
            presence = SmtpEmailConfiguration.GetVariablePresence(config)
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous().DisableRateLimiting();

app.MapGet("/reset-password", (HttpContext ctx) =>
{
    var token = ctx.Request.Query["token"].ToString();
    return string.IsNullOrWhiteSpace(token)
        ? Results.Redirect("/Account/ForgotPassword")
        : Results.Redirect($"/Account/ResetPassword?token={Uri.EscapeDataString(token)}");
}).AllowAnonymous().DisableRateLimiting();

app.MapGet("/confirm-email", (HttpContext ctx) =>
{
    var userId = ctx.Request.Query["userId"].ToString();
    var token = ctx.Request.Query["token"].ToString();
    if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        return Results.Redirect("/Account/ConfirmEmail");
    return Results.Redirect(
        $"/Account/ConfirmEmail?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}");
}).AllowAnonymous().DisableRateLimiting();

app.MapPost("/api/stripe/webhook", async (HttpContext ctx, IStripeBillingService billing, ILogger<Program> logger) =>
{
    if (!billing.IsConfigured)
        return Results.NotFound();

    using var reader = new StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();
    var signature = ctx.Request.Headers["Stripe-Signature"].ToString();
    try
    {
        await billing.HandleWebhookAsync(json, signature);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Stripe webhook rejected");
        return Results.BadRequest();
    }
}).AllowAnonymous();
app.MapClinicalApi();
app.MapAuthOtpApi();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapRazorPages();
app.Run();

public partial class Program { }
