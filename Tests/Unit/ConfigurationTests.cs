using BLL.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Unit;

public class ConfigurationTests
{
    [Fact]
    public void JwtOptions_Properties_SetCorrectly()
    {
        // Arrange & Act
        var options = new JwtOptions
        {
            Key = "test-key-12345",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpirationMinutes = 60
        };

        // Assert
        Assert.Equal("test-key-12345", options.Key);
        Assert.Equal("test-issuer", options.Issuer);
        Assert.Equal("test-audience", options.Audience);
        Assert.Equal(60, options.ExpirationMinutes);
    }

    [Fact]
    public void PaginationOptions_Properties_SetCorrectly()
    {
        // Arrange & Act
        var options = new PaginationOptions
        {
            DefaultPageSize = 20,
            MaxPageSize = 100
        };

        // Assert
        Assert.Equal(20, options.DefaultPageSize);
        Assert.Equal(100, options.MaxPageSize);
    }

    [Fact]
    public void RabbitMqOptions_Properties_SetCorrectly()
    {
        // Arrange & Act
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            QueueName = "test-queue",
            Durable = true
        };

        // Assert
        Assert.Equal("localhost", options.HostName);
        Assert.Equal(5672, options.Port);
        Assert.Equal("guest", options.UserName);
        Assert.Equal("guest", options.Password);
        Assert.Equal("test-queue", options.QueueName);
        Assert.True(options.Durable);
    }

    [Fact]
    public void RabbitMqOptions_GetConnectionString_ReturnsCorrectFormat()
    {
        // Arrange
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "admin",
            Password = "secret"
        };

        // Act
        var connectionString = options.GetConnectionString();

        // Assert
        Assert.Contains("localhost", connectionString);
        Assert.Contains("5672", connectionString);
        Assert.Contains("admin", connectionString);
        Assert.Contains("secret", connectionString);
    }

    [Fact]
    public void ResiliencePolicies_CreateRabbitMqPolicy_ReturnsPolicy()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        var policy = ResiliencePolicies.CreateRabbitMqPolicy(logger.Object);

        // Assert
        Assert.NotNull(policy);
    }
}
