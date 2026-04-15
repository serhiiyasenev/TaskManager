using BLL.Configuration;
using BLL.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Polly;
using Xunit;

namespace Tests.Unit;

public class ResilienceAndQueueTests
{
    [Fact]
    public async Task ResiliencePolicies_Retries_And_InvokesLogger()
    {
        var logger = new Mock<ILogger>();
        var policy = ResiliencePolicies.CreateRabbitMqPolicy(logger.Object);
        var attempts = 0;

        await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new InvalidOperationException("retry");
            }

            return Task.CompletedTask;
        });

        Assert.Equal(2, attempts);
        logger.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task RabbitMqService_PostValue_OnPublishFailure_ReturnsFalse()
    {
        var options = Options.Create(new RabbitMqOptions
        {
            HostName = "invalid host name with spaces",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            QueueName = "q"
        });
        var logger = Mock.Of<ILogger<RabbitMqService>>();
        var service = new RabbitMqService(options, logger, Policy.NoOpAsync());

        var result = await service.PostValue("payload");

        Assert.False(result);
    }
}
