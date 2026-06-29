using AtoZClinical.Infrastructure.Services;
using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class SmtpEmailSettingsTests
{
    [Fact]
    public void From_IsReady_When_Host_From_And_Credentials_Set()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "smtp.sendgrid.net",
                ["Email:FromAddress"] = "noreply@example.com",
                ["Email:SmtpUser"] = "apikey",
                ["Email:SmtpPassword"] = "secret",
                ["Email:Enabled"] = "true"
            })
            .Build();

        var settings = SmtpEmailSettings.From(config);

        Assert.True(settings.IsReady);
        Assert.Equal("ready", settings.DescribeReadiness());
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, settings.SecureSocketOptions);
    }

    [Fact]
    public void From_NotReady_Without_Smtp_Credentials()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "smtp.gmail.com",
                ["Email:FromAddress"] = "noreply@example.com",
                ["Email:Enabled"] = "true"
            })
            .Build();

        var settings = SmtpEmailSettings.From(config);

        Assert.False(settings.IsReady);
        Assert.Equal("missing SMTP_USER", settings.DescribeReadiness());
    }

    [Fact]
    public void Bootstrap_Maps_Render_Environment_Variables()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.mailgun.org");
        Environment.SetEnvironmentVariable("SMTP_PORT", "587");
        Environment.SetEnvironmentVariable("SMTP_USER", "postmaster");
        Environment.SetEnvironmentVariable("SMTP_PASS", "key");
        Environment.SetEnvironmentVariable("FROM_EMAIL", "hello@clinic.test");
        try
        {
            var config = new ConfigurationManager();
            SmtpConfigurationBootstrap.Apply(config);

            var settings = SmtpEmailSettings.From(config);
            Assert.True(settings.IsReady);
            Assert.Equal("smtp.mailgun.org", settings.Host);
            Assert.Equal("hello@clinic.test", settings.FromAddress);
            Assert.Equal("postmaster", settings.User);
            Assert.Equal("key", settings.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", null);
            Environment.SetEnvironmentVariable("SMTP_PORT", null);
            Environment.SetEnvironmentVariable("SMTP_USER", null);
            Environment.SetEnvironmentVariable("SMTP_PASS", null);
            Environment.SetEnvironmentVariable("FROM_EMAIL", null);
        }
    }

    [Fact]
    public void From_Uses_SmtpUser_As_From_For_Gmail()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "smtp.gmail.com",
                ["Email:FromAddress"] = "wrong@example.com",
                ["Email:SmtpUser"] = "user@gmail.com",
                ["Email:SmtpPassword"] = "app-password",
                ["Email:Enabled"] = "true"
            })
            .Build();

        var settings = SmtpEmailSettings.From(config);

        Assert.True(settings.IsReady);
        Assert.Equal("user@gmail.com", settings.FromAddress);
        Assert.Contains("Gmail", settings.ConfigurationWarning ?? string.Empty);
    }

    [Fact]
    public void ClassifyFailure_Identifies_Authentication_Error()
    {
        var ex = new AuthenticationException("Invalid credentials");
        Assert.Contains("authentication", SmtpEmailDiagnostics.ClassifyFailure(ex), StringComparison.OrdinalIgnoreCase);
    }
}
