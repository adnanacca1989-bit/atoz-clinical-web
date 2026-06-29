using AtoZClinical.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace AtoZClinical.Tests;

public class SmtpEmailSettingsTests
{
    [Fact]
    public void From_Enables_When_Smtp_Env_Vars_Present()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.sendgrid.net");
        Environment.SetEnvironmentVariable("FROM_EMAIL", "noreply@example.com");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Enabled"] = "false"
                })
                .Build();

            var settings = SmtpEmailSettings.From(config);

            Assert.True(settings.Enabled);
            Assert.True(settings.IsReady);
            Assert.Equal("smtp.sendgrid.net", settings.Host);
            Assert.Equal("noreply@example.com", settings.FromAddress);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", null);
            Environment.SetEnvironmentVariable("FROM_EMAIL", null);
        }
    }

    [Fact]
    public void From_Reads_Legacy_Email_Config_Keys()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST", null);
        Environment.SetEnvironmentVariable("FROM_EMAIL", null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "smtp.mailgun.org",
                ["Email:FromAddress"] = "hello@clinic.test",
                ["Email:SmtpPort"] = "587",
                ["Email:SmtpUser"] = "user",
                ["Email:SmtpPassword"] = "secret"
            })
            .Build();

        var settings = SmtpEmailSettings.From(config);

        Assert.True(settings.IsReady);
        Assert.Equal("smtp.mailgun.org", settings.Host);
        Assert.Equal(587, settings.Port);
        Assert.Equal("user", settings.User);
        Assert.Equal("secret", settings.Password);
    }
}
