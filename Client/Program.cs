using Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var menu = new MenuActions();
            menu.Greetings();

            var host = new HostBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddLogging(b => b.AddSimpleConsole());
                services.AddHostedService<SignalRListenerService>();
            })
            .Build();

            await host.StartAsync();

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
                Console.WriteLine("8. Start Timer Service To Execute Random Tasks With Delay");
                Console.WriteLine("9. Stop Timer Service");
                Console.WriteLine("10. Exit the program\n");
                Console.Write("Enter your choice:\n");

                if (int.TryParse(Console.ReadLine(), out var choiceInt) && choiceInt is >= 0 and <= 10)
                {
                    if (choiceInt == 10)
                    {
                        // Stop the host before exiting the program
                        await host.StopAsync();
                        Environment.Exit(0);
                    }

                    await menu.Actions[choiceInt]();
                }
                else
                {
                    MenuActions.PrintColored("Invalid choice. Please enter a number between 0 and 10.", ConsoleColor.Yellow);
                }
            }
        }
    }
}