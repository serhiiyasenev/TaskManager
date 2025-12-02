using Microsoft.AspNetCore.Identity;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class PasswordHashGenerator
{
    private readonly ITestOutputHelper _output;

    public PasswordHashGenerator(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GeneratePasswordHash()
    {
        var hasher = new PasswordHasher<object>();
        var hash = hasher.HashPassword(null, "Password1!");
        
        _output.WriteLine("New password hash for 'Password1!':");
        _output.WriteLine(hash);
        
        // Verify it works
        var result = hasher.VerifyHashedPassword(null, hash, "Password1!");
        Assert.Equal(PasswordVerificationResult.Success, result);
    }
}
