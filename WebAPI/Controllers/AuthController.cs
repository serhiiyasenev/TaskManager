using BLL.Interfaces;
using DAL.Entities;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPI.Authentication;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(BrowserAuthenticationExtensions.AuthRateLimitPolicy)]
public sealed class AuthController(
    UserManager<User> users,
    SignInManager<User> signIn,
    IAccountEmailSender email,
    IAntiforgery antiforgery,
    TimeProvider clock) : ControllerBase
{
    private const string GenericRecoveryMessage = "If the account is eligible, an email has been sent.";
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterAccountRequest request, CancellationToken ct)
    {
        var user = new User(request.UserName, request.FirstName, request.LastName, request.Email)
        {
            RegisteredAt = clock.GetUtcNow().UtcDateTime
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(result.Errors.GroupBy(error => AccountField(error.Code))
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()))
            { Title = "Registration could not be completed.", Status = StatusCodes.Status400BadRequest });
        }
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendConfirmationAsync(user.Email!, user.Id.ToString(), token, ct);
        return Accepted(new { message = "Check your email to confirm your account." });
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        var token = DecodeToken(request.Token);
        if (user is null || token is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid confirmation link.");
        }
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? Ok(new { message = "Email confirmed." })
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid confirmation link.");
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(EmailAccountRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null && !user.EmailConfirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await email.SendConfirmationAsync(user.Email!, user.Id.ToString(), token, ct);
        }
        return Accepted(new { message = GenericRecoveryMessage });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginAccountRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentialsMessage);
        }
        var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentialsMessage);
        }
        return Ok(await ProfileAsync(user));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        Response.Headers.CacheControl = "no-store";
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(await ProfileAsync(user));
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(EmailAccountRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null && user.EmailConfirmed)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendPasswordResetAsync(user.Email!, token, ct);
        }
        return Accepted(new { message = GenericRecoveryMessage });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        var token = DecodeToken(request.Token);
        if (user is null || token is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid password reset request.");
        }
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid password reset request.");
        }
        // A verified recovery link restores access to locked accounts, including
        // preserved legacy accounts locked during removal of demo credentials.
        if (users.SupportsUserLockout)
        {
            var unlocked = await users.SetLockoutEndDateAsync(user, null);
            var resetFailures = await users.ResetAccessFailedCountAsync(user);
            if (!unlocked.Succeeded || !resetFailures.Succeeded)
            {
                return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Account recovery could not be completed.");
            }
        }
        return Ok(new { message = "Password reset. Sign in with your new password." });
    }

    [HttpPost("change-password")]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(result.Errors.GroupBy(error => error.Code == "PasswordMismatch" ? "currentPassword" : "newPassword")
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()))
            { Title = "Password could not be changed.", Status = StatusCodes.Status400BadRequest });
        }
        await signIn.RefreshSignInAsync(user);
        return Ok(new { message = "Password changed." });
    }

    private async Task<SessionProfile> ProfileAsync(User user) => new(user.Id, user.UserName, user.Email,
        user.FirstName, user.LastName, user.TeamId, user.EmailConfirmed, (await users.GetRolesAsync(user)).ToArray());

    private static string? DecodeToken(string token)
    {
        try { return SmtpAccountEmailSender.DecodeToken(token); }
        catch (FormatException) { return null; }
    }

    private static string AccountField(string code) => code.Contains("Password", StringComparison.Ordinal) ? "password"
        : code.Contains("Email", StringComparison.Ordinal) ? "email"
        : code.Contains("UserName", StringComparison.Ordinal) ? "userName" : "account";
}
