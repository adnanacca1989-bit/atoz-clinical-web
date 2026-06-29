using AtoZClinical.Infrastructure.Services;
using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class SmtpEmailSettingsTests
{
    [Fact]
    public void IsEmailConfigured_True_When_All_Variables_Set()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "smtp.sendgrid.net",
                ["Email:SmtpPort"] = "587",
                ["Email:FromAddress"] = "noreply@example.com",
                ["Email:SmtpUser"] = "apikey",
                ["Email:SmtpPassword"] = "secret"
            })
            .Build();

        Assert.True(SmtpEmailConfiguration.IsEmailConfigured(config));
        Assert.Equal(SecureSocketOptions.StartTls, SmtpEmailSettings.From(config).SecureSocketOptions);
    }

    [Fact]
    public void IsEmailConfigured_False_Without_Smtp_Credentials()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "smtp.gmail.com",
                ["Email:SmtpPort"] = "587",
                ["Email:FromAddress"] = "noreply@example.com"
            })
            .Build();

        Assert.False(SmtpEmailConfiguration.IsEmailConfigured(config));
        Assert.Contains("SMTP_USER", SmtpEmailConfiguration.GetMissingVariables(config));
        Assert.Contains("SMTP_PASS", SmtpEmailConfiguration.GetMissingVariables(config));
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

            Assert.True(SmtpEmailConfiguration.IsEmailConfigured(config));
            var settings = SmtpEmailSettings.From(config);
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
    public void From_Uses_SmtpUser_As_From_For_Gmail_When_From_Missing()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PORT", "587");
        Environment.SetEnvironmentVariable("SMTP_USER", "user@gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PASS", "app-password");
        Environment.SetEnvironmentVariable("FROM_EMAIL", null);
        try
        {
            var config = new ConfigurationBuilder().Build();
            Assert.True(SmtpEmailConfiguration.IsEmailConfigured(config));
            Assert.Equal("user@gmail.com", SmtpEmailSettings.From(config).FromAddress);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", null);
            Environment.SetEnvironmentVariable("SMTP_PORT", null);
            Environment.SetEnvironmentVariable("SMTP_USER", null);
            Environment.SetEnvironmentVariable("SMTP_PASS", null);
        }
    }

    [Fact]
    public void ClassifyFailure_Identifies_Authentication_Error()
    {
        var ex = new AuthenticationException("Invalid credentials");
        Assert.Contains("authentication", SmtpEmailDiagnostics.ClassifyFailure(ex), StringComparison.OrdinalIgnoreCase);
    }
}
