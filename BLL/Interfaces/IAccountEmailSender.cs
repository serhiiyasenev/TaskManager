namespace BLL.Interfaces;

/// <summary>Account mail delivery without exposing links or tokens in application logs.</summary>
public interface IAccountEmailSender
{
    Task SendConfirmationAsync(string email, string userId, string token, CancellationToken ct = default);
    Task SendPasswordResetAsync(string email, string token, CancellationToken ct = default);
}
