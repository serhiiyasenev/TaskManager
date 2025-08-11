using Notifier;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<RabbitMqListenerService>();
builder.Services.AddSignalR();
var app = builder.Build();

app.MapHub<ChatHub>("/chathub");

app.MapGet("/", () => "Hello, world! SignalR - ChatHub is working now!");

app.Run();
