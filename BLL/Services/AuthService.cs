using AutoMapper;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Models.Users;
using DAL.Entities;
using Go.BLL.DTOs;
using Go.BLL.DTOs.Results;
using Go.BLL.Services.Interfaces;
using Go.DAL.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Go.BLL.Services;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IConfiguration configuration,
    IMapper mapper)
    : IAuthService
{
    public async Task<string> RegisterAsync(RegisterUserDto model)
    {
        var user = new User(model.FirstName, model.LastName, model.Email);
        var result = await userManager.CreateAsync(user, model.Password);

        return !result.Succeeded ? string.Join(" ", result.Errors.Select(e => e.Description).ToList()) : "User registered successfully.";
    }

    public async Task<UserLoginInfoDto> LoginAsync(LoginUserDto model)
    {
        var user = await userManager.FindByEmailAsync(model.Email);
        if (user == null)
            throw new NotFoundException("No user found. Please register.");

        var signIn = await signInManager.CheckPasswordSignInAsync(user, model.Password, true);

        if (signIn.IsLockedOut)
            throw new NotFoundException("Account is locked. Try again later.");

        if (signIn.IsNotAllowed)
            throw new NotFoundException("Sign-in is not allowed for this account.");

        if (!signIn.Succeeded)
            throw new NotFoundException("Incorrect password, please try again.");

        var token = GenerateJwtToken(user);

        return new UserLoginInfoDto { Token = token, Id = user.Id };
    }

    public async Task<FullUserDto> GetFullUserInfoAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user == null ? throw new NotFoundException("No user found.") : mapper.Map<FullUserDto>(user);
    }

    public async Task<FullUserDto> GetPublicUserInfoAsync(int userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new NotFoundException("No user found.");
        }

        return mapper.Map<FullUserDto>(user);
    }

    private string GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim("firstName", user.FirstName),
            new Claim("lastName", user.LastName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}