namespace TaskManager.WebHost;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        // Test-first host scaffold; no proxy or SPA behavior exists yet.
        await app.RunAsync();
    }
}
