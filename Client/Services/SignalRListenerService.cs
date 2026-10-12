using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Client.Services;

public class SignalRListenerService(HubConnection connection, ILogger<SignalRListenerService> log, ConsoleSession session) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        connection.On<string, string>("ReceiveMessage", (user, message) =>
        {
            if (!session.IsAuthenticated) return Task.CompletedTask;
            Console.WriteLine("\n---Message was received from Notifier---");
            Console.WriteLine($"{user}: {message}\n");
            return Task.CompletedTask;
        });

        await Task.Delay(5000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!session.IsAuthenticated)
            {
                if (connection.State != HubConnectionState.Disconnected) await connection.StopAsync(stoppingToken);
                await Task.Delay(1000, stoppingToken);
                continue;
            }
            if (connection.State is not HubConnectionState.Connected
                and not HubConnectionState.Connecting)
            {
                try
                {
                    await connection.StartAsync(stoppingToken);
                    log.LogInformation("SignalR connected");
                }
                catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    session.Logout();
                    log.LogWarning("Notification session expired. Sign in again.");
                    continue;
                }
                catch (Exception) when (!stoppingToken.IsCancellationRequested)
                {
                    log.LogWarning("Notification hub unavailable. Retrying after 3 seconds.");
                    await Task.Delay(3000, stoppingToken);
                    continue;
                }
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (connection.State == HubConnectionState.Connected) await connection.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
