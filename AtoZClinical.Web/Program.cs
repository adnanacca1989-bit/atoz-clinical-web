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
builder.Services.AddExceptionHandler<DataProtectionExceptionHandler>();

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
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 15;
        limiter.QueueLimit = 0;
    });
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
builder.Services.AddScoped<IClinicalEmailSender, SmtpClinicalEmailSender>();
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
builder.Services.AddScoped<BillingPropagationService>();
builder.Services.AddScoped<PatientVisitHistoryService>();
builder.Services.AddScoped<InvoiceDeleteGuardService>();
builder.Services.AddScoped<PatientVisitStatusService>();
builder.Services.AddScoped<FormPermissionService>();
builder.Services.AddScoped<ClinicModuleService>();
builder.Services.AddScoped<DoctorReportService>();
builder.Services.AddScoped<GlobalTransactionSearchService>();
builder.Services.AddScoped<FormPermissionPageFilter>();
builder.Services.AddScoped<ReportBrandingPageFilter>();
builder.Services.AddScoped<ClinicBrandingPageFilter>();
builder.Services.AddScoped<PharmacyCogsService>();
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
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Account/LicenseBlocked");
    options.Conventions.AllowAnonymousToPage("/Account/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/Account/ResetPassword");
    options.Conventions.AllowAnonymousToPage("/Account/ConfirmEmail");
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
await ClinicalDataProtectionSetup.WarmUpAsync(app.Services, useSqlite, app.Logger);

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else if (builder.Configuration.GetValue("UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<LanguageMiddleware>();
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<LoginCookieResetMiddleware>();
app.UseMiddleware<DataProtectionRecoveryMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ClinicSubdomainMiddleware>();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<ClinicTenantMiddleware>();
app.UseMiddleware<FormPermissionMiddleware>();
app.UseAuthorization();
app.UseMiddleware<MfaEnforcementMiddleware>();
app.UseMiddleware<PhiAccessAuditMiddleware>();
app.UseMiddleware<OperationsMonitoringMiddleware>();
app.MapGet("/health", async (HttpContext ctx, ClinicalDbContext db, OperationalMetrics metrics, IConfiguration config) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync())
            return Results.Problem("Database unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);

        var basic = new Dictionary<string, object?>
        {
            ["status"] = "healthy",
            ["version"] = AppBuildInfo.Version,
            ["timestamp"] = DateTime.UtcNow
        };

        var token = config["Operations:HealthToken"];
        if (!string.IsNullOrWhiteSpace(token) &&
            ctx.Request.Headers.TryGetValue("X-Health-Token", out var provided) &&
            provided == token)
        {
            basic["database"] = "connected";
            basic["metrics"] = metrics.GetSnapshot();
        }

        return Results.Content(JsonSerializer.Serialize(basic), "application/json");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Health check failed");
        return Results.Problem("Health check failed", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
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
app.MapHub<ChatHub>("/hubs/chat");
app.MapRazorPages();
app.Run();

public partial class Program { }
