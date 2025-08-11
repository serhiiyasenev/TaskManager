using Client.Helpers;
using Client.Models;
using Client.Services;

namespace Client
{
    public class MenuActions
    {
        static HttpClient? HttpClient { get; set; }
        static ITimerService? TimerService { get; set; }

        public MenuActions()
        {
            HttpClient = new HttpClient();
            HttpClient.BaseAddress = new Uri("https://localhost:7151/api/");
        }

        public Dictionary<int, Func<Task>> Actions = new()
        {
            [0] = GetTasksCountInProjectsByUserId,
            [1] = GetCapitalTasksByUserId,
            [2] = GetProjectsByTeamSize,
            [3] = GetSortedTeamByMembersWithYear,
            [4] = GetSortedUsersWithSortedTasks,
            [5] = GetUserInfo,
            [6] = GetProjectsInfo,
            [7] = GetSortedFilteredPageOfProjects,
            [8] = StartTimerServiceToExecuteRandomTasksWithDelay,
            [9] = StopTimerService
        };

        public static async Task GetTasksCountInProjectsByUserId()
        {
            PrintColored($"\nProvide Id as Integer", ConsoleColor.Green);

            if (!int.TryParse(Console.ReadLine(), out var id))
            {
                PrintColored("\nIncorrect ID - use Integer", ConsoleColor.Red);
                return;
            }

            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetTasksCountInProjectsByUserId/{id}"));
            var tasks = await responseMessage.GetModelAsync<Dictionary<string, int>>();

            PrintColored($"\n{tasks?.ToPrintString()}", ConsoleColor.Green);
        }

        public static async Task GetCapitalTasksByUserId()
        {
            PrintColored($"\nProvide Id as Integer", ConsoleColor.Green);

            if (!int.TryParse(Console.ReadLine(), out var id))
            {
                PrintColored("\nIncorrect ID - use Integer", ConsoleColor.Red);
                return;
            }

            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetCapitalTasksByUserId/{id}"));
            var tasks = await responseMessage.GetModelAsync<List<TaskDto>>();

            tasks?.ForEach(e => PrintColored($"\n{e}", ConsoleColor.Green));
        }

        public static async Task GetProjectsByTeamSize()
        {
            PrintColored($"\nProvide TeamSize as Integer", ConsoleColor.Green);

            if (!int.TryParse(Console.ReadLine(), out var teamSize))
            {
                PrintColored("\nIncorrect TeamSize - use Integer", ConsoleColor.Red);
                return;
            }

            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetProjectsByTeamSize/{teamSize}"));
            var projects = await responseMessage.GetModelAsync<List<ProjectInfo>>();

            projects?.ForEach(e => PrintColored($"\n{e.Id}: {e.Name}", ConsoleColor.Green));
        }

        public static async Task GetSortedTeamByMembersWithYear()
        {
            PrintColored($"\nProvide year as Integer", ConsoleColor.Green);

            if (!int.TryParse(Console.ReadLine(), out var year))
            {
                PrintColored("\nIncorrect year - use Integer", ConsoleColor.Red);
                return;
            }

            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetSortedTeamByMembersWithYear/{year}"));
            var teams = await responseMessage.GetModelAsync<List<TeamWithMembersDto>>();

            teams?.ForEach(e => PrintColored($"\n{e}", ConsoleColor.Green));
        }

        public static async Task GetSortedUsersWithSortedTasks()
        {
            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetSortedUsersWithSortedTasks"));
            var users = await responseMessage.GetModelAsync<List<UserWithTasksDto>>();

            users?.ForEach(e => PrintColored($"\n{e}", ConsoleColor.Green));
        }

        public static async Task GetUserInfo()
        {
            PrintColored($"\nProvide Id as Integer", ConsoleColor.Green);

            if (!int.TryParse(Console.ReadLine(), out int id))
            {
                PrintColored("\nIncorrect ID - use Integer", ConsoleColor.Red);
                return;
            }

            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetUserInfo/{id}"));
            var userInfo = await responseMessage.GetModelAsync<UserInfoDto>();

            PrintColored($"\n{userInfo}", ConsoleColor.Green);
        }

        public static async Task GetProjectsInfo()
        {
            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetProjectsInfo"));
            var projects = await responseMessage.GetModelAsync<List<ProjectInfoDto>>();

            projects?.ForEach(e => PrintColored($"\n{e}", ConsoleColor.Green));
        }

        public static async Task StopTimerService()
        {
            if (TimerService != null)
            {
                TimerService.Stop();
                TimerService.Dispose();
                TimerService = null;
                await Console.Out.WriteLineAsync("\n Timer Service was stopped");
            }
            else
            {
                await Console.Out.WriteLineAsync("\n Timer Service wasn't started");
            }
        }

        public static async Task StartTimerServiceToExecuteRandomTasksWithDelay()
        {
            if (TimerService == null)
            {
                await MarkRandomTaskWithDelay(15_000);
            }
            else
            {
                await Console.Out.WriteLineAsync("\n Timer Service already started");
            }
        }

        private static async Task MarkRandomTaskWithDelay(int delayMilliseconds)
        {
            List<Func<Task>> methods =
            [
                GetSortedUsersWithSortedTasks,
                GetProjectsInfo,
                GetSortedFilteredPageOfProjects
            ];

            var queries = new Queries(methods, HttpClient);

            TimerService = new TimerService(delayMilliseconds);
            TimerService.Elapsed += queries.ExecuteRandomTask;
            TimerService.Start();

            await Console.Out.WriteLineAsync($"\n Timer Service Started with Delay/Interval in '{delayMilliseconds}' Milliseconds");
        }

        public static async Task GetSortedFilteredPageOfProjects()
        {
            var responseMessage = await HttpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"Analytics/GetSortedFilteredPageOfProjects?PageSize={25}&PageNumber={1}"));
            var projects = await responseMessage.GetModelAsync<PagedList<FullProjectDto>>();

            PrintColored($"\nTotalCount: {projects?.TotalCount}", ConsoleColor.Green);
            projects?.Items.ForEach(e => PrintColored($"\n{e}", ConsoleColor.Green));
        }

        public void Greetings()
        {
            Console.WriteLine("\n*******************************************************************************");
            Console.WriteLine("*                                                                             *");
            Console.WriteLine("*                             WELCOME TO COOL                                 *");
            Console.WriteLine("*                             PROJECT MANAGEMENT SYSTEM                       *");
            Console.WriteLine("*                                                                             *");
            Console.WriteLine("*******************************************************************************\n");
        }

        public static void PrintColored(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }
}
