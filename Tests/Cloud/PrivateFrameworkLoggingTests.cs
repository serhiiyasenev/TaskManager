using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TaskManager.Hosting;
using WebAPI;
using Xunit;

namespace Tests.Cloud;

public sealed class PrivateFrameworkLoggingTests
{
    private const string PrivateValue = "private-normalized-email-and-sql-value";

    [Fact]
    public void NativeLoggerPipelineDropsProviderFailuresAndKeepsSafeApplicationFailures()
    {
        var sink = new RecordingProvider();
        using var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace).AddProvider(sink).AddPrivateFrameworkLogging()
            // A more specific environment/configuration override must not undo privacy.
            .AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.Error));
        EmitProviderFailures(category => factory.CreateLogger(category));
        factory.CreateLogger("BLL.Services.TasksService").LogError("Task write failed; failure type {FailureType}", "DbUpdateException");
        Assert.Single(sink.Messages);
        Assert.Contains("DbUpdateException", sink.Messages[0]);
        Assert.DoesNotContain(PrivateValue, string.Join("\n", sink.Messages));
    }

    [Fact]
    public void SerilogPipelineAppliesPrivacyBeforeEverySink()
    {
        var sink = new RecordingSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose()
            .Filter.With(new PrivateFrameworkLogFilter()).WriteTo.Sink(sink).CreateLogger();
        foreach (var category in new[] { "Microsoft.EntityFrameworkCore.Update", "Microsoft.Data.SqlClient.EventSource", "System.Data.SqlClient" })
            logger.ForContext("SourceContext", category).Error(new InvalidOperationException(PrivateValue), "Database operation failed: {Value}", PrivateValue);
        logger.ForContext("SourceContext", "BLL.Services.TasksService").Error("Task write failed; failure type {FailureType}", "DbUpdateException");
        Assert.Single(sink.Events);
        Assert.Null(sink.Events[0].Exception);
        Assert.DoesNotContain(PrivateValue, sink.Events[0].RenderMessage());
    }

    private static void EmitProviderFailures(Func<string, Microsoft.Extensions.Logging.ILogger> createLogger)
    {
        foreach (var category in new[] { "Microsoft.EntityFrameworkCore.Update", "Microsoft.Data.SqlClient.EventSource", "System.Data.SqlClient" })
            createLogger(category).LogError(new InvalidOperationException(PrivateValue), "Database operation failed: {Value}", PrivateValue);
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class RecordingProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Recorder(Messages);
        public void Dispose() { }

        private sealed class Recorder(List<string> messages) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception) + exception?.Message);
        }
    }
}
