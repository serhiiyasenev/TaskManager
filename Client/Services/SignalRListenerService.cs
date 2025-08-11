using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;

namespace Client.Services;

public class SignalRListenerService(HubConnection connection) : BackgroundService
{
    private HubConnection _connection = connection;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for 5 seconds before connect to СhatHub
        await Task.Delay(5_000, stoppingToken); 

        _connection = new HubConnectionBuilder().WithUrl("https://localhost:7268/chathub").Build();

        _connection.On<string, string>("ReceiveMessage", (user, message) =>
        {
            Console.WriteLine("\n---Message was received from Notifier---");
            Console.WriteLine($"\n{user}: {message}");
        });

        // Connect to SignalR and start listening
        await _connection.StartAsync(stoppingToken);

        // Keep listening until the service is stopped
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        // Disconnect from SignalR when the service is being stopped
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.StopAsync(stoppingToken);
        }
    }
}