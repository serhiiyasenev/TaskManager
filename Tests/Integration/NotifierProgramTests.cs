extern alias NotifierLib;
using System.Net;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotifierLib::Notifier;
using Xunit;

namespace Tests.Integration;

public class NotifierProgramTests : IClassFixture<NotifierWebApplicationFactory>
{
    private readonly NotifierWebApplicationFactory _factory;

    public NotifierProgramTests(NotifierWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RootEndpoint_ReturnsWelcomeMessage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SignalR - ChatHub is working now", content);
    }
}

public class NotifierWebApplicationFactory : WebApplicationFactory<NotifierLib::Notifier.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IHostedService) &&
                     d.ImplementationType == typeof(NotifierLib::Notifier.RabbitMqListenerService));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IHostedService, NoOpHostedService>();
        });
    }

    private sealed class NoOpHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
