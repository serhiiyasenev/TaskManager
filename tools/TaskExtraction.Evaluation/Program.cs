using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BLL.Configuration;
using BLL.Models.AI;
using BLL.Services.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ExtractionEvaluation = BLL.Services.AI.TaskExtractionEvaluation;

namespace TaskManager.TaskExtractionEvaluation;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--run-live", StringComparer.Ordinal))
        { Console.Error.WriteLine("SKIP: live evaluation is disabled. Explicit --run-live and bounded token/cost budgets are required."); return 2; }
        if (!int.TryParse(Value(args, "--max-token-budget"), out var budget) || budget is < 1 or > 1_000_000)
        { Console.Error.WriteLine("Declare a token budget between 1 and 1000000 before live evaluation."); return 2; }
        if (!Price(Value(args, "--input-price-per-million"), out var inputPrice)
            || !Price(Value(args, "--output-price-per-million"), out var outputPrice)
            || !Price(Value(args, "--max-estimated-cost"), out var maxCost)
            || string.IsNullOrWhiteSpace(Value(args, "--currency")))
        { Console.Error.WriteLine("Declare input/output rates per million tokens, currency, and maximum estimated cost before live evaluation."); return 2; }
        var pricing = new TaskExtractionEvaluationPricing(inputPrice, outputPrice, Value(args, "--currency")!, maxCost);
        var fixturePath = Value(args, "--fixtures") ?? Path.Combine("evaluation", "task-extraction", "fixtures.json");
        var outputPath = Value(args, "--output") ?? Path.Combine(".local", "ai-evaluation-report.json");
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            pricing.Validate();
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var options = configuration.GetSection(TaskExtractionOptions.SectionName).Get<TaskExtractionOptions>() ?? new();
            options.Validate();
            if (options.Provider != "Azure" || string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.Deployment))
            { Console.Error.WriteLine("Configure the real Azure provider, endpoint, and deployment. Fake evaluation is not model evidence."); return 2; }
            var info = new FileInfo(fixturePath);
            if (!info.Exists || info.Length > 5_000_000) throw new InvalidOperationException();
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { MaxDepth = 10, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true };
            var fixtureJson = await File.ReadAllTextAsync(fixturePath, cancel.Token);
            var fixtureHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixtureJson))).ToLowerInvariant();
            var fixtures = JsonSerializer.Deserialize<FixtureDocument>(fixtureJson, jsonOptions)
                ?? throw new InvalidOperationException();
            if (fixtures.Version != 1 || fixtures.Cases.Count is < 30 or > 100
                || fixtures.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != fixtures.Cases.Count
                || fixtures.Cases.Any(c => !Regex.IsMatch(c.Id, "^[a-z]{2}-[a-z0-9-]{1,60}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
                throw new InvalidOperationException();
            var environment = new EvaluationEnvironment(configuration["DOTNET_ENVIRONMENT"] ?? "Development");
            var transport = new AzureSdkTaskExtractionChatTransport(options, environment);
            var provider = new AzureTaskExtractionProvider(options, environment, transport);
            // Preflight every reservation, without transmitting gold expectations.
            var reservations = fixtures.Cases.Select(c => provider.CalculateReservationTokens(c.Input())).ToArray();
            var totalReservation = reservations.Sum(value => (long)value);
            if (totalReservation > budget)
            { Console.Error.WriteLine("The declared token budget is below the complete conservative reservation. No inference was started."); return 2; }
            var preflightEstimatedCost = pricing.ReserveCost(totalReservation);
            if (preflightEstimatedCost > pricing.MaximumEstimatedCost)
            { Console.Error.WriteLine("The declared estimated cost cap is below the conservative preflight cost. No inference was started."); return 2; }
            var records = new List<CaseReport>();
            var remainingBudget = budget;
            string? stopReason = null;
            foreach (var (fixture, index) in fixtures.Cases.Select((value, index) => (value, index)))
            {
                if (stopReason is not null || cancel.IsCancellationRequested)
                { records.Add(new(fixture.Id, false, false, null, null, stopReason ?? "Ai.Canceled", 0, null, null, null, 0, 0m, false)); continue; }
                var reservation = reservations[index];
                remainingBudget -= reservation;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var response = await provider.ExtractAsync(fixture.Input(), cancel.Token);
                    var evaluation = ExtractionEvaluation.Evaluate(fixture.Input(), response, fixture.Expected, options.MaxItems);
                    var usageKnown = response.InputTokens is > 0 && response.OutputTokens is >= 0;
                    var actual = usageKnown ? checked(response.InputTokens!.Value + response.OutputTokens!.Value) : reservation;
                    var estimated = pricing.Estimate(response.InputTokens, response.OutputTokens, reservation);
                    if (usageKnown && actual <= reservation) remainingBudget += reservation - actual;
                    records.Add(new(fixture.Id, evaluation.StrictSchemaValid, evaluation.GroundedOutputValid, evaluation.RawScore,
                        evaluation.ValidatedScore, actual > reservation ? "Ai.ReservationExceeded" : evaluation.FailureCode, stopwatch.ElapsedMilliseconds, response.InputTokens,
                        response.OutputTokens, SafeModel(response.Model), reservation, estimated.Amount, estimated.UsesReportedUsage));
                    if (actual > reservation) { remainingBudget -= actual - reservation; stopReason = "Ai.ReservationExceeded"; }
                    if (records.Sum(record => record.EstimatedCost) > pricing.MaximumEstimatedCost)
                    {
                        stopReason = "Ai.EstimatedCostExceeded";
                        records[^1] = records[^1] with { FailureCode = stopReason };
                    }
                }
                catch (TaskExtractionProviderException error)
                {
                    var code = SafeCode(error.SafeCode);
                    records.Add(new(fixture.Id, false, false, null, null, code, stopwatch.ElapsedMilliseconds, null, null, null, reservation, pricing.ReserveCost(reservation), false));
                    if (code is "Ai.Authentication" or "Ai.Unavailable" or "Ai.RateLimited") stopReason = code;
                }
                catch (OperationCanceledException)
                { records.Add(new(fixture.Id, false, false, null, null, "Ai.Canceled", stopwatch.ElapsedMilliseconds, null, null, null, reservation, pricing.ReserveCost(reservation), false)); stopReason = "Ai.Canceled"; }
            }
            var report = new
            {
                formatVersion = 1, runType = "live_azure", completedAtUtc = DateTimeOffset.UtcNow,
                provider = "azure", deployment = options.Deployment, fixtureVersion = fixtures.Version, fixtureSha256 = fixtureHash,
                cases = records.Count, attempted = records.Count(c => c.ReservedTokens > 0),
                strictSchemaValid = records.Count(c => c.StrictSchemaValid), groundedOutputValid = records.Count(c => c.GroundedOutputValid),
                failures = records.Count(c => c.FailureCode is not null),
                raw = Aggregate(records.Select(c => c.RawScore), fixtures.Cases.Sum(c => c.Expected.Count)),
                validated = Aggregate(records.Select(c => c.ValidatedScore), fixtures.Cases.Sum(c => c.Expected.Count)),
                declaredTokenBudget = budget, retainedTokenReservation = budget - remainingBudget,
                knownInputTokens = records.Sum(c => (long)(c.InputTokens ?? 0)), knownOutputTokens = records.Sum(c => (long)(c.OutputTokens ?? 0)),
                usageKnownCases = records.Count(c => c.InputTokens is not null && c.OutputTokens is not null),
                currency = pricing.Currency, inputPricePerMillion = pricing.InputPricePerMillion,
                outputPricePerMillion = pricing.OutputPricePerMillion, maxEstimatedCost = pricing.MaximumEstimatedCost,
                preflightEstimatedCost, estimatedCost = records.Sum(record => record.EstimatedCost),
                costBasis = "configured_token_rates_not_invoice", discountsAndProvisionedChargesIncluded = false,
                elapsedMilliseconds = records.Sum(c => c.ElapsedMilliseconds), stopReason, results = records
            };
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { report.cases, report.attempted, report.strictSchemaValid, report.groundedOutputValid,
                report.failures, report.raw, report.validated, report.retainedTokenReservation, report.currency, report.estimatedCost,
                report.costBasis, report.stopReason }));
            return report.failures == 0 && stopReason is null && records.All(c => c.ValidatedScore?.UnauthorizedAssigneeCount == 0) ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Evaluation canceled."); return 1; }
        catch (Exception) { Console.Error.WriteLine("Evaluation configuration or execution failed. No raw provider details were logged."); return 2; }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static object Aggregate(IEnumerable<TaskExtractionEvaluationScore?> values, int expectedActions)
    {
        var scores = values.Where(value => value is not null).Select(value => value!).ToArray();
        var tp = scores.Sum(value => value.TruePositives); var fp = scores.Sum(value => value.FalsePositives);
        return new
        {
            truePositives = tp, falsePositives = fp, falseNegatives = expectedActions - tp,
            sourceTaskPrecision = tp + fp == 0 ? 0 : (double)tp / (tp + fp),
            sourceTaskRecall = expectedActions == 0 ? 0 : (double)tp / expectedActions,
            assigneeAccuracy = tp == 0 ? 0 : (double)scores.Sum(value => value.CorrectAssignees) / tp,
            deadlineAccuracy = tp == 0 ? 0 : (double)scores.Sum(value => value.CorrectDeadlines) / tp,
            inaccessibleAssigneeViolations = scores.Sum(value => value.UnauthorizedAssigneeCount)
        };
    }
    private static string? Value(string[] args, string name)
    { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static bool Price(string? value, out decimal amount)
        => decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    private static string? SafeModel(string? model) => model is not null && Regex.IsMatch(model, "^[a-zA-Z0-9._-]{1,128}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? model : null;
    private static string SafeCode(string code) => code is "Ai.Authentication" or "Ai.Unavailable" or "Ai.RateLimited" or "Ai.Transient"
        or "Ai.InvalidRequest" or "Ai.InvalidOutput" or "Ai.Timeout" or "Ai.InputLimit" or "Ai.InvalidTimeZone" ? code : "Ai.Unexpected";

    private sealed record FixtureDocument(int Version, string Description, IReadOnlyList<FixtureCase> Cases);
    private sealed record FixtureCase(string Id, string Locale, string Notes, DateOnly ReferenceDate, string TimeZoneId,
        IReadOnlyList<AllowedAiAssignee> AllowedAssignees, IReadOnlyList<TaskExtractionGoldTask> Expected)
    { public TaskExtractionProviderInput Input() => new(Notes, 1, ReferenceDate, TimeZoneId, Locale, AllowedAssignees); }
    private sealed record CaseReport(string Id, bool StrictSchemaValid, bool GroundedOutputValid, TaskExtractionEvaluationScore? RawScore,
        TaskExtractionEvaluationScore? ValidatedScore, string? FailureCode, long ElapsedMilliseconds, int? InputTokens, int? OutputTokens, string? Model,
        int ReservedTokens, decimal EstimatedCost, bool UsesReportedUsage);
    private sealed class EvaluationEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "TaskExtraction.Evaluation";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
