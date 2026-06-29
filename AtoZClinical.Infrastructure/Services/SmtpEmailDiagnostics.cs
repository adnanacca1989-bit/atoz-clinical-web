using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace AtoZClinical.Infrastructure.Services;

public static class SmtpEmailDiagnostics
{
    public const string UserFriendlyFailureMessage = "We could not send the email. Please try again later.";

    public static string ClassifyFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AuthenticationException:
                    return "SMTP authentication failed (check SMTP_USER and SMTP_PASS)";
                case SmtpCommandException smtp when smtp.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase):
                    return "SMTP authentication failed (check SMTP_USER and SMTP_PASS)";
                case SmtpCommandException smtp:
                    return $"SMTP server rejected message: {smtp.Message}";
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused }:
                    return "Connection refused (check SMTP_HOST and SMTP_PORT)";
                case SocketException { SocketErrorCode: SocketError.TimedOut }:
                    return "SMTP connection timed out";
                case SocketException socket:
                    return $"Network error: {socket.SocketErrorCode}";
                case IOException io:
                    return $"SMTP IO error: {io.Message}";
            }
        }

        return ex.Message;
    }

    public static bool IsGmailHost(string? host) =>
        host?.Contains("gmail", StringComparison.OrdinalIgnoreCase) == true;
}
