using AtoZClinical.Infrastructure.Services;
using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class SmtpEmailSettingsTests
{
    [Fact]
    public void FormatMissingConfigurationError_lists_required_render_variables()
    {
        var text = SmtpEmailConfiguration.FormatMissingConfigurationError(["SMTP_HOST", "SMTP_PASS"]);
        Assert.Contains("SMTP is not configured", text);
        Assert.Contains("Render Web Service", text);
        Assert.Contains("SMTP_HOST", text);
        Assert.Contains("SMTP_PASS", text);
    }

    [Fact]
    public void FormatNotConfiguredLogMessage_uses_expected_prefix()
    {
        var text = SmtpEmailConfiguration.FormatNotConfiguredLogMessage(["SMTP_HOST", "SMTP_FROM"]);
        Assert.Equal("Email is NOT CONFIGURED — missing: SMTP_HOST, SMTP_FROM", text);
    }

    [Fact]
    public void FormatMissingVariablesText_Lists_Each_Variable_As_Bullet()
    {
        var text = SmtpEmailConfiguration.FormatMissingVariablesText(["SMTP_HOST", "SMTP_USER"]);
        Assert.Contains("Missing:", text);
        Assert.Contains("* SMTP_HOST", text);
        Assert.Contains("* SMTP_USER", text);
    }

    [Fact]
    public void IsEmailConfigured_True_When_All_Smtp_Env_Variables_Set()
    {
        SetSmtpEnv("smtp.sendgrid.net", "587", "apikey", "secret", "noreply@example.com");
        try
        {
            Assert.True(SmtpEmailConfiguration.IsEmailConfigured());
            Assert.Equal(SecureSocketOptions.StartTls, SmtpEmailSettings.From().SecureSocketOptions);
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void IsEmailConfigured_False_Without_Smtp_Credentials()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PORT", "587");
        Environment.SetEnvironmentVariable("SMTP_FROM", "noreply@example.com");
        try
        {
            Assert.False(SmtpEmailConfiguration.IsEmailConfigured());
            var missing = SmtpEmailConfiguration.GetMissingVariables();
            Assert.Contains("SMTP_USER", missing);
            Assert.Contains("SMTP_PASS", missing);
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void IsEmailConfigured_False_Without_SMTP_FROM()
    {
        SetSmtpEnv("smtp.gmail.com", "587", "user@gmail.com", "secret", from: null);
        try
        {
            Assert.False(SmtpEmailConfiguration.IsEmailConfigured());
            Assert.Contains("SMTP_FROM", SmtpEmailConfiguration.GetMissingVariables());
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void BuildEmailHealthPayload_reports_missing_variables()
    {
        SetSmtpEnv("smtp.gmail.com", "587", "user@gmail.com", "secret", from: null);
        try
        {
            var payload = SmtpEmailConfiguration.BuildEmailHealthPayload();
            Assert.False((bool)payload["emailConfigured"]!);
            Assert.Equal("not_configured", payload["status"]);
            var missing = Assert.IsType<List<string>>(payload["missingVariables"]);
            Assert.Contains("SMTP_FROM", missing);
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void GetVariablePresence_Flags_Invalid_Port()
    {
        SetSmtpEnv("smtp.gmail.com", "not-a-number", "user@gmail.com", "secret", "user@gmail.com");
        try
        {
            var presence = SmtpEmailConfiguration.GetVariablePresence();
            Assert.True(presence["SMTP_HOST"]);
            Assert.False(presence["SMTP_PORT"]);
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void Bootstrap_Maps_Render_Smtp_Environment_Variables()
    {
        SetSmtpEnv("smtp.mailgun.org", "587", "postmaster", "key", "hello@clinic.test");
        try
        {
            var config = new ConfigurationManager();
            SmtpConfigurationBootstrap.Apply(config);

            Assert.True(SmtpEmailConfiguration.IsEmailConfigured(config));
            var settings = SmtpEmailSettings.From(config);
            Assert.Equal("smtp.mailgun.org", settings.Host);
            Assert.Equal("hello@clinic.test", settings.FromAddress);
            Assert.Equal("postmaster", settings.User);
            Assert.Equal("key", settings.Password);
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void From_Uses_SmtpUser_As_From_For_Gmail_When_SmtpFrom_Missing_at_send_time()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PORT", "587");
        Environment.SetEnvironmentVariable("SMTP_USER", "user@gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PASS", "app-password");
        Environment.SetEnvironmentVariable("SMTP_FROM", null);
        try
        {
            Assert.False(SmtpEmailConfiguration.IsEmailConfigured());
            Assert.Equal("user@gmail.com", SmtpEmailSettings.From().FromAddress);
        }
        finally
        {
            ClearSmtpEnv();
        }
    }

    [Fact]
    public void ClassifyFailure_Identifies_Authentication_Error()
    {
        var ex = new AuthenticationException("Invalid credentials");
        Assert.Contains("authentication", SmtpEmailDiagnostics.ClassifyFailure(ex), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetSmtpEnv(
        string host,
        string port,
        string user,
        string pass,
        string? from)
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", host);
        Environment.SetEnvironmentVariable("SMTP_PORT", port);
        Environment.SetEnvironmentVariable("SMTP_USER", user);
        Environment.SetEnvironmentVariable("SMTP_PASS", pass);
        Environment.SetEnvironmentVariable("SMTP_FROM", from);
    }

    private static void ClearSmtpEnv()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", null);
        Environment.SetEnvironmentVariable("SMTP_PORT", null);
        Environment.SetEnvironmentVariable("SMTP_USER", null);
        Environment.SetEnvironmentVariable("SMTP_PASS", null);
        Environment.SetEnvironmentVariable("SMTP_FROM", null);
    }
}
