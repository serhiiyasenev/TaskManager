using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace TaskManager.FullStackHost;

internal sealed record FullStackSettings(Guid RunId, string RepositoryRoot, string StatePath, string MasterConnection,
    string Transport, string RabbitHost, int RabbitPort, string RabbitUser, string RabbitPassword)
{
    public string DatabaseName => "taskmanager_e2e_test_" + RunId.ToString("N");
    public string TaskQueue => DatabaseName + ".tasks";
    public string ReminderQueue => DatabaseName + ".reminders";
    public string WorkingDirectory => Path.Combine(Path.GetDirectoryName(StatePath)!, "host-" + RunId.ToString("N"));
    public string RunDirectory => Path.GetDirectoryName(StatePath)!;
    public string CleanupPath => Path.Combine(Path.GetDirectoryName(StatePath)!, "cleanup.json");

    public static FullStackSettings Read()
    {
        if (Environment.GetEnvironmentVariable("TASKMANAGER_FULLSTACK_ENABLE") != "1")
            throw new InvalidOperationException("Full-stack verification requires explicit enablement.");
        var rawSql = Required("TASKMANAGER_FULLSTACK_SQL");
        var sql = new SqlConnectionStringBuilder(rawSql);
        if ((!string.IsNullOrWhiteSpace(sql.InitialCatalog) && !sql.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(sql.AttachDBFilename) || !IsLoopbackSql(sql.DataSource))
            throw new InvalidOperationException("Full-stack SQL requires an explicit loopback master connection.");
        sql.InitialCatalog = "master";
        sql.ConnectTimeout = 10;
        var root = FindRepository();
        var state = Required("TASKMANAGER_FULLSTACK_STATE");
        if (!Path.IsPathFullyQualified(state) || File.Exists(state))
            throw new InvalidOperationException("Full-stack state requires a fresh absolute private file path.");
        state = Path.GetFullPath(state);
        var runDirectory = new DirectoryInfo(Path.GetDirectoryName(state)!);
        var expectedParent = Path.GetFullPath(Path.Combine(root, ".local", "fullstack"));
        if (!Path.GetFileName(state).Equals("state.json", StringComparison.Ordinal)
            || !string.Equals(runDirectory.Parent?.FullName, expectedParent, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !Guid.TryParseExact(runDirectory.Name, "N", out var runId)
            || runDirectory.Name != runId.ToString("N")
            || File.Exists(Path.Combine(runDirectory.FullName, "cleanup.json")))
            throw new InvalidOperationException("Full-stack state must be a fresh owned repository run directory.");
        var workDirectory = Path.Combine(runDirectory.FullName, "host-" + runId.ToString("N"));
        if (PrivateFiles.PathEntryExists(workDirectory))
            throw new InvalidOperationException("Full-stack work directory must be newly owned by this host.");
        foreach (var directory in new[] { Path.Combine(root, ".local"), expectedParent, runDirectory.FullName,
                     workDirectory, Path.Combine(workDirectory, "keyring") })
            PrivateFiles.AssertNoDirectoryLinks(directory);
        var transport = Required("TASKMANAGER_FULLSTACK_TRANSPORT");
        if (transport is not ("Controlled" or "RabbitMQ")) throw new InvalidOperationException("Full-stack transport is invalid.");
        if (!File.Exists(Path.Combine(root, "frontend", "dist", "index.html")))
            throw new InvalidOperationException("Build the real frontend before starting full-stack verification.");
        var host = Environment.GetEnvironmentVariable("TASKMANAGER_FULLSTACK_RABBIT_HOST") ?? "127.0.0.1";
        if (transport == "RabbitMQ" && !(host is "localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Full-stack RabbitMQ requires a loopback disposable broker.");
        var portText = Environment.GetEnvironmentVariable("TASKMANAGER_FULLSTACK_RABBIT_PORT") ?? "5672";
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Full-stack RabbitMQ port is invalid.");
        var user = transport == "RabbitMQ" ? Required("TASKMANAGER_FULLSTACK_RABBIT_USER") : "unused";
        var password = transport == "RabbitMQ" ? Required("TASKMANAGER_FULLSTACK_RABBIT_PASSWORD") : "unused";
        return new(runId, root, state, sql.ConnectionString, transport, host, port, user, password);
    }

    private static string Required(string name) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
        ? Environment.GetEnvironmentVariable(name)! : throw new InvalidOperationException("Required full-stack configuration is absent.");

    private static bool IsLoopbackSql(string source)
    {
        var host = source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? source[4..] : source;
        host = host.Split(',', '\\')[0];
        return host is "." or "(local)" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    private static string FindRepository()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AI", "plan.md"))
                    && File.Exists(Path.Combine(directory.FullName, "WebAPI", "WebAPI.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Full-stack repository root is unavailable.");
    }
}

internal static class PrivateFiles
{
    public static bool PathEntryExists(string path)
    {
        // Exists follows links and returns false for dangling targets. Link
        // metadata/attributes must still reject that pre-existing path entry.
        if (new DirectoryInfo(path).LinkTarget is not null) return true;
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static void AssertNoDirectoryLinks(string path)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
        {
            if (directory.LinkTarget is not null)
                throw new InvalidOperationException("Private full-stack directories cannot traverse links or junctions.");
            try
            {
                if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Private full-stack directories cannot traverse links or junctions.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static void CreateDirectory(string path)
    {
        AssertNoDirectoryLinks(path);
        Directory.CreateDirectory(path);
        AssertNoDirectoryLinks(path);
        if (OperatingSystem.IsWindows())
        {
            var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Private file owner unavailable.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static async Task WriteNewJsonAsync<T>(string path, T value, CancellationToken ct = default)
    {
        var parent = Path.GetDirectoryName(path)!;
        AssertNoDirectoryLinks(parent);
        Directory.CreateDirectory(parent);
        AssertNoDirectoryLinks(parent);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                // On Windows the already-private owned run directory grants
                // inherited access solely to its current-user owner. Set this
                // before file creation, rather than reopening a FileShare.None
                // stream to change its ACL while it is being written.
                await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal sealed class ReservedPort : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    public int Port { get; }
    public ReservedPort() { _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; }
    public void Dispose() => _listener.Stop();
}
