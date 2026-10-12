using Azure;
using Azure.Communication.Email;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Moq;
using WebAPI.Authentication;
using Xunit;

namespace Tests.Cloud;

public sealed class AzureEmailTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeEmail_UsesCompletedSingleOperationAndFixedPublicOrigin(bool reset)
    {
        var client = new Mock<EmailClient>();
        EmailMessage? sent = null;
        client.Setup(c => c.SendAsync(WaitUntil.Completed, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WaitUntil, EmailMessage, CancellationToken>((_, message, _) => sent = message)
            .ReturnsAsync(Operation(EmailSendStatus.Succeeded));
        var sender = Sender(client.Object);
        if (reset) await sender.SendPasswordResetAsync("recipient@example.test", "synthetic+/reset token");
        else await sender.SendConfirmationAsync("recipient@example.test", "42", "synthetic+/confirmation token");

        Assert.NotNull(sent);
        Assert.Equal("verified@sender.example.test", sent.SenderAddress);
        Assert.Single(sent.Recipients.To);
        Assert.Equal("recipient@example.test", sent.Recipients.To[0].Address);
        var text = sent.Content.PlainText;
        var uri = new Uri(text[text.IndexOf("https://", StringComparison.Ordinal)..]);
        Assert.Equal("https://public.example.test", uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal(reset ? "/reset-password" : "/confirm-email", uri.AbsolutePath);
        var token = QueryHelpers.ParseQuery(uri.Query)["token"].ToString();
        Assert.Equal(reset ? "synthetic+/reset token" : "synthetic+/confirmation token", SmtpAccountEmailSender.DecodeToken(token));
        client.Verify(c => c.SendAsync(WaitUntil.Completed, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Running")]
    public async Task NonSuccessfulCompletionCannotBeReportedAsSuccess(string status)
    {
        var client = new Mock<EmailClient>();
        client.Setup(c => c.SendAsync(WaitUntil.Completed, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Operation(new EmailSendStatus(status)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(client.Object)
            .SendConfirmationAsync("recipient@example.test", "42", "synthetic-secret"));
    }

    [Fact]
    public async Task UnknownSendOutcomeNeverStartsAnotherOperationOrLeaksProviderBody()
    {
        var client = new Mock<EmailClient>();
        client.Setup(c => c.SendAsync(WaitUntil.Completed, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "private-token cookie notes synthetic-secret"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(client.Object)
            .SendPasswordResetAsync("recipient@example.test", "synthetic-secret"));
        Assert.DoesNotContain("synthetic-secret", error.ToString());
        client.Verify(c => c.SendAsync(WaitUntil.Completed, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelledCallerDoesNotStartNativeSend()
    {
        var client = new Mock<EmailClient>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(client.Object)
            .SendConfirmationAsync("recipient@example.test", "42", "synthetic-secret", cancellation.Token));
        client.VerifyNoOtherCalls();
    }

    private static EmailSendOperation Operation(EmailSendStatus status)
    {
        var operation = new Mock<EmailSendOperation>();
        operation.SetupGet(o => o.HasCompleted).Returns(status != EmailSendStatus.Running);
        operation.SetupGet(o => o.HasValue).Returns(true);
        operation.SetupGet(o => o.Value).Returns(EmailModelFactory.EmailSendResult("synthetic-operation", status));
        return operation.Object;
    }

    private static AzureCommunicationAccountEmailSender Sender(EmailClient client) => new(client,
        Options.Create(new AccountEmailOptions
        {
            Provider = "AzureCommunication", FromAddress = "verified@sender.example.test",
            AzureCommunicationEndpoint = "https://synthetic.communication.azure.com", SendTimeoutSeconds = 30
        }), Options.Create(new BrowserAuthenticationOptions
        {
            PublicOrigin = "https://public.example.test", AdditionalAllowedOrigins = ["https://candidate.example.test"]
        }), new CloudTestEnvironment());
}
