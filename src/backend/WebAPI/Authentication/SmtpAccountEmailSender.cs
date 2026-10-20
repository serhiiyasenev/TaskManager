using System.Net;
using System.Net.Mail;
using System.Text;
using BLL.Interfaces;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace WebAPI.Authentication;

public sealed class SmtpAccountEmailSender(
    IOptions<AccountEmailOptions> emailOptions,
    IOptions<BrowserAuthenticationOptions> browserOptions,
    IHostEnvironment environment) : IAccountEmailSender
{
    public Task SendConfirmationAsync(string email, string userId, string token, CancellationToken ct = default)
    {
        var link = BuildLink("/confirm-email", new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["token"] = EncodeToken(token)
        });
        return SendAsync(email, "Підтвердження email TaskManager", $"Підтвердьте вашу адресу email: {link}", ct);
    }

    public Task SendPasswordResetAsync(string email, string token, CancellationToken ct = default)
    {
        var link = BuildLink("/reset-password", new Dictionary<string, string?>
        {
            ["email"] = email,
            ["token"] = EncodeToken(token)
        });
        return SendAsync(email, "Відновлення пароля TaskManager", $"Змінити пароль: {link}", ct);
    }

    private string BuildLink(string path, Dictionary<string, string?> query)
    {
        var origin = BrowserAuthenticationExtensions.ValidatePublicOrigin(browserOptions.Value.PublicOrigin, environment);
        return QueryHelpers.AddQueryString(new Uri(origin, path).AbsoluteUri, query);
    }

    private async Task SendAsync(string email, string subject, string body, CancellationToken ct)
    {
        var options = emailOptions.Value;
        if (string.IsNullOrWhiteSpace(options.SmtpHost) || options.SmtpPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("Email SMTP host/port is not configured.");
        }
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !options.UseTls)
        {
            throw new InvalidOperationException("Production account email requires SMTP TLS.");
        }
        using var message = new MailMessage(new MailAddress(options.FromAddress), new MailAddress(email))
        {
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        using var smtp = new SmtpClient(options.SmtpHost, options.SmtpPort) { EnableSsl = options.UseTls };
        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            smtp.Credentials = new NetworkCredential(options.UserName, options.Password);
        }
        await smtp.SendMailAsync(message, ct);
    }

    public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    public static string DecodeToken(string token) => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
}
