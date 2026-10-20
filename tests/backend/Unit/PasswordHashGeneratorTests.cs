using Microsoft.AspNetCore.Identity;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Unit;

public class PasswordHashGeneratorTests(ITestOutputHelper output)
{
    [Fact]
    public void GeneratePasswordHash()
    {
        var hasher = new PasswordHasher<object>();
        var hash = hasher.HashPassword(null!, "Password1!");
        
        output.WriteLine("New password hash for 'Password1!':");
        output.WriteLine(hash);
        
        // Verify it works
        var result = hasher.VerifyHashedPassword(null!, hash, "Password1!");
        Assert.Equal(PasswordVerificationResult.Success, result);
    }
}
