using AtoZClinical.Web.Middleware;
using AtoZClinical.Infrastructure;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

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

var rawConnectionString = FirstNonEmpty(
    builder.Configuration.GetConnectionString("ClinicalDatabase"),
    builder.Configuration["DATABASE_URL"],
    Environment.GetEnvironmentVariable("ConnectionStrings__ClinicalDatabase"),
    Environment.GetEnvironmentVariable("DATABASE_URL"),
    Environment.GetEnvironmentVariable("RENDER_DATABASE_URL"));

if (string.IsNullOrWhiteSpace(rawConnectionString))
    throw new InvalidOperationException(
        "Database connection string is missing. Set ConnectionStrings__ClinicalDatabase or DATABASE_URL on Render.");

var connectionString = NormalizeConnectionString(rawConnectionString);
var useSqlite = builder.Configuration.GetValue("Database:Provider", "PostgreSQL") == "Sqlite"
    || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<ClinicalDbContext>(options =>
{
    if (useSqlite)
        options.UseSqlite(connectionString);
    else
        options.UseNpgsql(connectionString);
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ClinicalDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ClinicalRoles.Vendor, policy => policy.RequireRole(ClinicalRoles.Vendor));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ClinicContextService>();
builder.Services.AddScoped<ClinicAccessService>();
builder.Services.AddScoped<VendorClinicService>();
builder.Services.AddScoped<PatientService>();
builder.Services.AddScoped<AppointmentService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.AddScoped<ServiceIncomeService>();
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
builder.Services.AddScoped<ClinicSettingsService>();
builder.Services.AddScoped<ClinicLookupService>();
builder.Services.AddScoped<PharmacyPurchaseBillService>();
builder.Services.AddScoped<ClinicBackupService>();
builder.Services.AddHostedService<ClinicLicenseMaintenanceService>();
builder.Services.AddRazorPages(options =>
{
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
    options.Conventions.AuthorizeFolder("/Pharmacy");
    options.Conventions.AuthorizeFolder("/Settings");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/LicenseBlocked");
    options.Conventions.AllowAnonymousToPage("/Register/Clinic");
    options.Conventions.AllowAnonymousToPage("/Index");
});

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

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
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<ClinicTenantMiddleware>();
app.UseAuthorization();
app.MapRazorPages();
app.Run();
