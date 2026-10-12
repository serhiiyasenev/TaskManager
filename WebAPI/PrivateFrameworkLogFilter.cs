using Serilog.Core;
using Serilog.Events;
using TaskManager.Hosting;

namespace WebAPI;

public sealed class PrivateFrameworkLogFilter : ILogEventFilter
{
    public bool IsEnabled(LogEvent logEvent) =>
        !logEvent.Properties.TryGetValue("SourceContext", out var context) ||
        context is not ScalarValue { Value: string category } ||
        !PrivateFrameworkLogging.ContainsPrivateProviderData(category);
}
