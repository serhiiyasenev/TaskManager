using Client.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Client;

public class Program
{
    static async Task Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(b => b.AddSimpleConsole());
                var endpoints = ClientConnectionOptions.FromConfiguration(context.Configuration);
                services.AddSingleton(new HttpClient { BaseAddress = endpoints.ApiBaseUrl });
                services.AddSingleton<ConsoleSession>();
                services.AddSingleton<MenuActions>();

                services.AddSingleton(sp =>
                    new HubConnectionBuilder()
                        .WithUrl(endpoints.HubUrl, options => options.AccessTokenProvider =
                            () => Task.FromResult(sp.GetRequiredService<ConsoleSession>().AccessToken))
                        .WithAutomaticReconnect()
                        .Build()
                );

                services.AddHostedService<SignalRListenerService>();
            }).Build();

        await host.StartAsync();
        var menu = host.Services.GetRequiredService<MenuActions>();
        var session = host.Services.GetRequiredService<ConsoleSession>();
        var connection = host.Services.GetRequiredService<HubConnection>();
        menu.Greetings();

        while (true)
        {
            Console.WriteLine("\n0. Get Tasks Count In Projects By User Id");
            Console.WriteLine("1. Get Capital Tasks By User Id");
            Console.WriteLine("2. Get Projects By Team Size");
            Console.WriteLine("3. Get Sorted Team By Members With Year");
            Console.WriteLine("4. Get Sorted Users With Sorted Tasks");
            Console.WriteLine("5. Get User Info");
            Console.WriteLine("6. Get Projects Info");
            Console.WriteLine("7. Get Sorted Filtered Page Of Projects");
            Console.WriteLine("8. Get Task Status By Project Id");
            Console.WriteLine("9. Start Timer Service To Execute Random Tasks With Delay");
            Console.WriteLine("10. Stop Timer Service");
            Console.WriteLine("11. List Tasks With Reminder State");
            Console.WriteLine("12. Update Task Reminder Settings");
            Console.WriteLine("13. Exit the program");
            Console.WriteLine("14. Sign in with email/password");
            Console.WriteLine("15. Sign out\n");
            Console.Write("Enter your choice:\n");

            if (int.TryParse(Console.ReadLine(), out var choiceInt) && choiceInt is >= 0 and <= 15)
            {
                if (choiceInt == 13)
                {
                    // Stop the host before exiting the program
                    await host.StopAsync();
                    session.Logout();
                    return;
                }

                if (menu.Actions.TryGetValue(choiceInt, out var action))
                {
                    if (choiceInt < 13 && !session.IsAuthenticated)
                    {
                        MenuActions.PrintColored("Sign in with option 14 first.", ConsoleColor.Yellow);
                        continue;
                    }
                    try
                    {
                        await action();
                        if (choiceInt == 15) await connection.StopAsync();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        session.Logout();
                        await connection.StopAsync();
                        MenuActions.PrintColored("Session expired. Sign in again with option 14.", ConsoleColor.Yellow);
                    }
                    catch (HttpRequestException error)
                    {
                        MenuActions.PrintColored(error.StatusCode == System.Net.HttpStatusCode.Forbidden
                            ? "You do not have access to this operation." : "Request failed. Check the service and try again.", ConsoleColor.Red);
                    }
                }
                else
                {
                    MenuActions.PrintColored("Invalid choice. Please select an existing option.", ConsoleColor.Yellow);
                }
            }
            else
            {
                MenuActions.PrintColored("Invalid choice. Please enter a number between 0 and 15.", ConsoleColor.Yellow);
            }
        }
    }
}
