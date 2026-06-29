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
    public void IsEmailConfigured_True_When_Render_Uses_Email_Double_Underscore_Names()
    {
        Environment.SetEnvironmentVariable("Email__SmtpHost", "smtp.sendgrid.net");
        Environment.SetEnvironmentVariable("Email__SmtpPort", "587");
        Environment.SetEnvironmentVariable("Email__SmtpUser", "apikey");
        Environment.SetEnvironmentVariable("Email__SmtpPassword", "secret");
        Environment.SetEnvironmentVariable("Email__FromAddress", "noreply@example.com");
        try
        {
            var config = new ConfigurationBuilder().Build();
            Assert.True(SmtpEmailConfiguration.IsEmailConfigured(config));
            var presence = SmtpEmailConfiguration.GetVariablePresence(config);
            Assert.True(presence["SMTP_HOST"]);
            Assert.True(presence["SMTP_PORT"]);
            Assert.True(presence["SMTP_USER"]);
            Assert.True(presence["SMTP_PASS"]);
            Assert.True(presence["FROM_EMAIL"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Email__SmtpHost", null);
            Environment.SetEnvironmentVariable("Email__SmtpPort", null);
            Environment.SetEnvironmentVariable("Email__SmtpUser", null);
            Environment.SetEnvironmentVariable("Email__SmtpPassword", null);
            Environment.SetEnvironmentVariable("Email__FromAddress", null);
        }
    }

    [Fact]
    public void GetVariablePresence_Flags_Invalid_Port()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PORT", "not-a-number");
        Environment.SetEnvironmentVariable("SMTP_USER", "user@gmail.com");
        Environment.SetEnvironmentVariable("SMTP_PASS", "secret");
        Environment.SetEnvironmentVariable("FROM_EMAIL", "user@gmail.com");
        try
        {
            var presence = SmtpEmailConfiguration.GetVariablePresence();
            Assert.True(presence["SMTP_HOST"]);
            Assert.False(presence["SMTP_PORT"]);
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
