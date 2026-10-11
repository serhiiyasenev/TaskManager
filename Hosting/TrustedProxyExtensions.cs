using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskManager.Hosting;

public static class TrustedProxyExtensions
{
    public static IServiceCollection AddTrustedProxyForwarding(this IServiceCollection services, IConfiguration configuration)
    {
        // Test-first scaffold: trust validation and forwarding are not implemented yet.
        return services;
    }
}
