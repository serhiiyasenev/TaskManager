using BLL.Configuration;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Models.Users;
using DAL.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BLL.Services;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger)
    : IAuthService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<string> RegisterAsync(RegisterUserDto model, CancellationToken ct = default)
    {
        var user = new User(model.UserName, model.FirstName, model.LastName, model.Email)
        {
            RegisteredAt = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join(" ", result.Errors.Select(e => e.Description));
            logger.LogWarning("User registration failed for {Email}: {Errors}", model.Email, errors);
            return errors;
        }

        logger.LogInformation("User registered successfully: {Email}", model.Email);
        return "User registered successfully.";
    }

    public async Task<UserLoginInfoDto> LoginAsync(LoginUserDto model, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            logger.LogWarning("Login attempt for non-existent user: {Email}", model.Email);
            throw new NotFoundException("No user found. Please register.");
        }

        var signIn = await signInManager.CheckPasswordSignInAsync(user, model.Password, true);

        if (signIn.IsLockedOut)
        {
            logger.LogWarning("Login attempt for locked out user: {Email}", model.Email);
            throw new NotFoundException("Account is locked. Try again later.");
        }

        if (signIn.IsNotAllowed)
        {
            logger.LogWarning("Login not allowed for user: {Email}", model.Email);
            throw new NotFoundException("Sign-in is not allowed for this account.");
        }

        if (!signIn.Succeeded)
        {
            logger.LogError("Incorrect password for user {Email}", model.Email);
            throw new NotFoundException("Incorrect password, please try again.");
        }

        var roles = await userManager.GetRolesAsync(user);
        var token = GenerateJwtToken(user, roles);

        logger.LogInformation("User logged in successfully: {Email}", model.Email);
        return new UserLoginInfoDto { Token = token, Id = user.Id };
    }

    private string GenerateJwtToken(User user, IList<string> roles)
    {
        try
        {
            logger.LogInformation("Generating JWT token for user {UserId}, key length: {KeyLength}", 
                user.Id, _jwtOptions.Key?.Length ?? 0);

            var claims = new List<Claim>
            {
                new (JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new (JwtRegisteredClaimNames.Email, user.Email!),
                new (JwtRegisteredClaimNames.PreferredUsername, user.UserName!),
                new ("firstName", user.FirstName),
                new ("lastName", user.LastName)
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            // Use SHA256 hash of the key to ensure it's exactly 256 bits (32 bytes)
            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(_jwtOptions.Key));
            logger.LogInformation("Key bytes length after SHA256: {BytesLength}", keyBytes.Length);
            
            var key = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(_jwtOptions.ExpirationMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating JWT token for user {UserId}", user.Id);
            throw;
        }
    }
}