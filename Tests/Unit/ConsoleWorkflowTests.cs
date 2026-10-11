using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Client;
using Client.Services;
using Xunit;

namespace Tests.Unit;

[CollectionDefinition("Console workflows", DisableParallelization = true)]
public sealed class ConsoleWorkflowCollection { }

[Collection("Console workflows")]
public class ConsoleWorkflowTests
{
    private const string DueDate = "2026-12-11T16:45:00Z";
    private const string SyntheticToken = "synthetic-console-token";

    [Fact]
    public async Task TaskList_ShowsAssigneeStateDeadlineAndReminderSettings()
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, new[]
        {
            TaskBody("Review API", "ToDo", true), TaskBody("Prepare migration", "Done", false)
        }));
        await flow.Menu.Actions[11]();
        Assert.Equal("/api/Tasks", Assert.Single(flow.Requests).Path);
        Assert.Contains("Review API", flow.Output);
        Assert.Contains("State: ToDo", flow.Output);
        Assert.Contains("Performer: Synthetic Member", flow.Output);
        Assert.Contains("Reminder: ON (offset 15m, sent no)", flow.Output);
        Assert.Contains("Escalation: ON (delay 30m, sent no)", flow.Output);
        Assert.Contains("State: Done", flow.Output);
        Assert.Contains("Reminder: OFF", flow.Output);
    }

    [Fact]
    public async Task EmptyTaskList_ShowsRecoveryState()
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, Array.Empty<object>()));
        await flow.Menu.Actions[11]();
        Assert.Contains("No tasks found.", flow.Output);
    }

    [Fact]
    public async Task ReminderUpdate_SendsChosenUtcDateAndValidatedFlags()
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, new { id = 42 }),
            "42\n" + DueDate + "\nyes\nnot-a-number\n15\nn\n\n");
        await flow.Menu.Actions[12]();
        var request = Assert.Single(flow.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/api/Tasks/42/reminder", request.Path);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(DateTime.Parse(DueDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), body.RootElement.GetProperty("dueDate").GetDateTime());
        Assert.True(body.RootElement.GetProperty("reminderEnabled").GetBoolean());
        Assert.Equal(15, body.RootElement.GetProperty("reminderOffsetMinutes").GetInt32());
        Assert.False(body.RootElement.GetProperty("escalationEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("escalationDelayMinutes").ValueKind);
        Assert.Contains("Invalid number", flow.Output);
        Assert.Contains("Reminder settings updated.", flow.Output);
    }

    [Theory]
    [InlineData("not-an-id\n", "Invalid Task Id.")]
    [InlineData("42\nnot-a-date\n", "Invalid date format.")]
    public async Task InvalidReminderInput_DoesNotSendMutation(string input, string error)
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, null), input);
        await flow.Menu.Actions[12]();
        Assert.Empty(flow.Requests);
        Assert.Contains(error, flow.Output);
    }

    [Fact]
    public async Task BlankDueDate_PreservesExistingDeadlineAsPromptPromises()
    {
        using var flow = new ConsoleFlow(request => request.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, TaskBody("Review API", "ToDo", true))
            : (HttpStatusCode.OK, new { id = 42 }), "42\n\nn\n\nn\n\n");
        await flow.Menu.Actions[12]();
        var request = Assert.Single(flow.Requests.Where(request => request.Method == HttpMethod.Put));
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(DueDate, body.RootElement.GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task UnauthorizedTaskList_ClearsSessionAndUsesExpiredSessionSignal()
    {
        using var flow = new ConsoleFlow(request => request.Path.EndsWith("/Users/Login")
            ? (HttpStatusCode.OK, new { id = 7, token = SyntheticToken })
            : (HttpStatusCode.Unauthorized, new { title = "Unauthorized" }));
        Assert.True(await flow.Session.LoginAsync("member@example.test", "synthetic password"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => flow.Menu.Actions[11]());
        Assert.Null(flow.Session.AccessToken);
        Assert.Null(flow.Session.UserId);
        Assert.Null(flow.Session.Client.DefaultRequestHeaders.Authorization);
        Assert.DoesNotContain(SyntheticToken, flow.Output);
    }

    [Fact]
    public async Task ProjectTaskReport_PrintsEveryStateAndCorrectTotals()
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, new[]
        {
            new { projectId = 1, projectName = "Release", toDo = 1, inProgress = 2, done = 3, canceled = 4, total = 10 }
        }), "1\n");
        await flow.Menu.Actions[8]();
        Assert.Equal("/api/Analytics/GetTaskStatusByProject/1", Assert.Single(flow.Requests).Path);
        var report = flow.Output.Split('\n').Single(line => line.StartsWith("| Release"));
        Assert.Equal(new[] { "Release", "1", "2", "3", "4", "10" },
            report.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(cell => cell.Trim()).ToArray());
        Assert.Contains("InProgress", flow.Output);
        Assert.Contains("Canceled", flow.Output);
    }

    [Fact]
    public async Task EmptyProjectTaskReport_ExplainsMissingData()
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, Array.Empty<object>()), "1\n");
        await flow.Menu.Actions[8]();
        Assert.Contains("No data for this project", flow.Output);
    }

    [Fact]
    public async Task ProjectPage_PreservesServerOrderAndReportsPaginationTotal()
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, new
        {
            items = new[]
            {
                new { id = 1, name = "Alpha release", description = "Synthetic", createdAt = DueDate, deadline = DueDate, tasks = Array.Empty<object>(), author = UserBody(), team = new { id = 1, name = "Delivery", createdAt = DueDate } },
                new { id = 2, name = "Beta release", description = "Synthetic", createdAt = DueDate, deadline = DueDate, tasks = Array.Empty<object>(), author = UserBody(), team = new { id = 1, name = "Delivery", createdAt = DueDate } }
            },
            totalCount = 12
        }));
        await flow.Menu.Actions[7]();
        Assert.Equal("/api/Analytics/GetSortedFilteredPageOfProjects?PageSize=25&PageNumber=1", Assert.Single(flow.Requests).Path);
        Assert.Contains("TotalCount: 12", flow.Output);
        Assert.True(flow.Output.IndexOf("Alpha release", StringComparison.Ordinal) < flow.Output.IndexOf("Beta release", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "7\n", "/api/Analytics/GetTasksCountInProjectsByUserId/7", "Release - tasks count: 3")]
    [InlineData(1, "7\n", "/api/Analytics/GetCapitalTasksByUserId/7", "Review API")]
    [InlineData(2, "2\n", "/api/Analytics/GetProjectsByTeamSize/2", "1: Release")]
    [InlineData(3, "2026\n", "/api/Analytics/GetSortedTeamByMembersWithYear/2026", "TeamName: Delivery")]
    [InlineData(4, "", "/api/Analytics/GetSortedUsersWithSortedTasks", "FirstName: Synthetic")]
    [InlineData(5, "7\n", "/api/Analytics/GetUserInfo/7", "Release")]
    [InlineData(6, "", "/api/Analytics/GetProjectsInfo", "Release")]
    public async Task AnalyticsMenu_UsesSelectedApiContractAndShowsResult(int selection, string input, string path, string visible)
    {
        object body = selection switch
        {
            0 => new Dictionary<string, int> { ["Release"] = 3 },
            1 => new[] { TaskBody("Review API", "ToDo", true) },
            2 => new[] { new { id = 1, name = "Release" } },
            3 => new[] { new { id = 1, name = "Delivery", members = new[] { UserBody() } } },
            4 => new[] { new { id = 7, firstName = "Synthetic", lastName = "Member", email = "member@example.test", registeredAt = DueDate, birthDay = "2000-01-01T00:00:00Z", tasks = new[] { TaskBody("Review API", "ToDo", true) } } },
            5 => new { user = UserBody(), lastProject = ProjectBody(), lastProjectTasksCount = 3, notFinishedOrCanceledTasksCount = 1, longestTask = TaskBody("Review API", "ToDo", true) },
            _ => new[] { new { project = ProjectBody(), longestTaskByDescription = TaskBody("Review API", "ToDo", true), shortestTaskByName = TaskBody("Review API", "ToDo", true), teamMembersCount = 2 } }
        };
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, body), input);
        await flow.Menu.Actions[selection]();
        Assert.Equal(path, Assert.Single(flow.Requests).Path);
        Assert.Contains(visible, flow.Output);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(8)]
    public async Task InvalidAnalyticsIdentifier_ExplainsInputErrorWithoutRequest(int selection)
    {
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, null), "not-a-number\n");
        await flow.Menu.Actions[selection]();
        Assert.Empty(flow.Requests);
        Assert.Contains("Incorrect", flow.Output);
    }

    [Fact]
    public async Task LoginAndLogout_StopBackgroundActionsWithoutPrintingCredentials()
    {
        const string password = "synthetic password only";
        using var flow = new ConsoleFlow(_ => (HttpStatusCode.OK, new { id = 7, token = SyntheticToken }), "member@example.test\n" + password + "\n");
        await flow.Menu.Actions[14]();
        Assert.True(flow.Session.IsAuthenticated);
        Assert.Contains("Signed in.", flow.Output);
        await flow.Menu.Actions[14]();
        Assert.Single(flow.Requests);
        Assert.Contains("Sign out before", flow.Output);
        await flow.Menu.Actions[9]();
        await flow.Menu.Actions[9]();
        Assert.Contains("Timer Service already started", flow.Output);
        await flow.Menu.Actions[15]();
        Assert.False(flow.Session.IsAuthenticated);
        Assert.Contains("Timer Service was stopped", flow.Output);
        Assert.Contains("Signed out.", flow.Output);
        Assert.DoesNotContain(password, flow.Output);
        Assert.DoesNotContain(SyntheticToken, flow.Output);
    }

    private static object UserBody() => new { id = 7, userName = "member", email = "member@example.test", teamId = 1, firstName = "Synthetic", lastName = "Member", registeredAt = DueDate, birthDay = "2000-01-01T00:00:00Z" };
    private static object ProjectBody() => new { id = 1, name = "Release", description = "Synthetic", createdAt = DueDate, deadline = DueDate };
    private static object TaskBody(string name, string state, bool reminders) => new
    {
        id = 42, projectId = 1, performerId = 7, name, description = "Synthetic test", state,
        createdAt = "2026-10-11T00:00:00Z", finishedAt = state == "Done" ? DueDate : null,
        dueDate = reminders ? DueDate : null, reminderEnabled = reminders, reminderOffsetMinutes = 15,
        reminderSentAt = (string?)null, escalationEnabled = reminders, escalationDelayMinutes = 30,
        escalationSentAt = (string?)null, performer = UserBody()
    };

    private sealed record CapturedRequest(HttpMethod Method, string Path, string? Body);

    private sealed class ConsoleFlow : IDisposable
    {
        private readonly TextReader _originalInput = Console.In;
        private readonly TextWriter _originalOutput = Console.Out;
        private readonly StringWriter _output = new(CultureInfo.InvariantCulture);
        public MenuActions Menu { get; }
        public ConsoleSession Session { get; }
        public List<CapturedRequest> Requests { get; } = [];
        public string Output => _output.ToString();

        public ConsoleFlow(Func<CapturedRequest, (HttpStatusCode Status, object? Body)> response, string input = "")
        {
            var http = new HttpClient(new WorkflowHttpHandler(Requests, response)) { BaseAddress = new Uri("https://example.test/api/") };
            Session = new ConsoleSession(http);
            Menu = new MenuActions(Session);
            Console.SetIn(new StringReader(input));
            Console.SetOut(_output);
        }

        public void Dispose()
        {
            try { MenuActions.StopTimerService().GetAwaiter().GetResult(); }
            finally
            {
                Console.SetIn(_originalInput);
                Console.SetOut(_originalOutput);
                Session.Client.Dispose();
                _output.Dispose();
            }
        }
    }

    private sealed class WorkflowHttpHandler(List<CapturedRequest> requests,
        Func<CapturedRequest, (HttpStatusCode Status, object? Body)> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(request.Method, request.RequestUri!.PathAndQuery,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            requests.Add(captured);
            var result = response(captured);
            return new HttpResponseMessage(result.Status) { Content = JsonContent.Create(result.Body) };
        }
    }
}
