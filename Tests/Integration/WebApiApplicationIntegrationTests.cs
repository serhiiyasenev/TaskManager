using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tests.Integration
{
    public class WebApiApplicationIntegrationTests : IClassFixture<WebApiApplicationFactory>
    {
        private readonly HttpClient _client;

        public WebApiApplicationIntegrationTests(WebApiApplicationFactory factory)
        {
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public async Task SwaggerEndpoint_IsAvailable()
        {
            var response = await _client.GetAsync("/swagger/v1/swagger.json");
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task CorrelationIdHeader_WhenProvided_IsEchoedBack()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.Add("X-Correlation-ID", "cid-123");

            var response = await _client.SendAsync(request);

            Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
            Assert.Equal("cid-123", values.Single());
        }

        [Fact]
        public async Task CorrelationIdHeader_WhenMissing_IsGenerated()
        {
            var response = await _client.GetAsync("/health/live");

            Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
            Assert.False(string.IsNullOrWhiteSpace(values.Single()));
        }

    }

    public class WebApiApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BootstrapAdmin:Enabled"] = "false",
                    ["ConnectionStrings:DbConnection"] = "Server=fake;Database=fake;",
                    ["Jwt:Key"] = "integration-test-jwt-key"
                });
            });
        }
    }
}
