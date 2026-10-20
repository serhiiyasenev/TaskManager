using Azure;
using Azure.Communication.Email;
using BLL.Interfaces;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace WebAPI.Authentication;

public sealed class AzureCommunicationAccountEmailSender(
    EmailClient client,
    IOptions<AccountEmailOptions> emailOptions,
    IOptions<BrowserAuthenticationOptions> browserOptions,
    IHostEnvironment environment) : IAccountEmailSender
{
    public Task SendConfirmationAsync(string email, string userId, string token, CancellationToken ct = default) =>
        SendAsync(email, "Підтвердження email TaskManager", "Підтвердьте вашу адресу email: ", "/confirm-email",
            new Dictionary<string, string?> { ["userId"] = userId, ["token"] = SmtpAccountEmailSender.EncodeToken(token) }, ct);

    public Task SendPasswordResetAsync(string email, string token, CancellationToken ct = default) =>
        SendAsync(email, "Відновлення пароля TaskManager", "Змінити пароль: ", "/reset-password",
            new Dictionary<string, string?> { ["email"] = email, ["token"] = SmtpAccountEmailSender.EncodeToken(token) }, ct);

    private async Task SendAsync(string email, string subject, string text, string path,
        Dictionary<string, string?> query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = emailOptions.Value;
        if (options.SendTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException("Email:SendTimeoutSeconds must be between 1 and 120.");
        var origin = BrowserAuthenticationExtensions.ValidatePublicOrigin(browserOptions.Value.PublicOrigin, environment);
        var link = QueryHelpers.AddQueryString(new Uri(origin, path).AbsoluteUri, query);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.SendTimeoutSeconds));
        var message = new EmailMessage(new MailAddress(options.FromAddress).Address,
            new EmailRecipients([new EmailAddress(new MailAddress(email).Address)]),
            new EmailContent(subject) { PlainText = text + link });
        try
        {
            // The SDK polls the same operation. Unknown submission outcomes are not resubmitted.
            var operation = await client.SendAsync(WaitUntil.Completed, message, timeout.Token);
            if (!operation.HasCompleted || !operation.HasValue || operation.Value.Status != EmailSendStatus.Succeeded)
                throw new InvalidOperationException("Account email was not accepted for delivery.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Account email delivery outcome is unknown. Retry through the account recovery flow.");
        }
        catch (RequestFailedException)
        {
            throw new InvalidOperationException("Account email was not accepted or its delivery outcome is unknown.");
        }
    }
}
