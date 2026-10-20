using System.ComponentModel.DataAnnotations;

namespace WebAPI.Authentication;

public sealed record RegisterAccountRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string UserName,
    [property: Required, StringLength(100)] string FirstName,
    [property: Required, StringLength(100)] string LastName,
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, StringLength(256, MinimumLength = 8)] string Password);

public sealed record LoginAccountRequest(
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, StringLength(256)] string Password);

public sealed record EmailAccountRequest([property: Required, EmailAddress, StringLength(256)] string Email);
public sealed record ConfirmEmailRequest([property: Required] string UserId, [property: Required, StringLength(4096)] string Token);
public sealed record ResetPasswordRequest(
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, StringLength(4096)] string Token,
    [property: Required, StringLength(256, MinimumLength = 8)] string NewPassword);
public sealed record ChangePasswordRequest(
    [property: Required, StringLength(256)] string CurrentPassword,
    [property: Required, StringLength(256, MinimumLength = 8)] string NewPassword);
public sealed record SessionProfile(int Id, string? UserName, string? Email, string FirstName, string LastName,
    int? TeamId, bool EmailConfirmed, IReadOnlyList<string> Roles);
