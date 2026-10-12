using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace TaskManager.Hosting;

public static class PrivateFrameworkLogging
{
    private static readonly string[] Categories =
        ["Microsoft.EntityFrameworkCore", "Microsoft.Data.SqlClient", "System.Data.SqlClient"];

    // SensitiveDataLogging=false does not redact SQL exception messages: a
    // duplicate-key error can include a private email or another indexed value.
    public static bool ContainsPrivateProviderData(string? category) => category is not null &&
        Categories.Any(prefix => category.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));

    public static ILoggingBuilder AddPrivateFrameworkLogging(this ILoggingBuilder builder)
    {
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            // Longer category/provider rules otherwise override family filters.
            // Apply to final options, including reloads and sink-specific settings.
            var providers = options.Rules.Select(rule => rule.ProviderName).Append(null).Distinct().ToArray();
            for (var index = options.Rules.Count - 1; index >= 0; index--)
                if (ContainsPrivateProviderData(options.Rules[index].CategoryName)) options.Rules.RemoveAt(index);
            foreach (var provider in providers)
                foreach (var category in Categories)
                    options.Rules.Add(new LoggerFilterRule(provider, category, LogLevel.None, null));
        });
        return builder;
    }
}
