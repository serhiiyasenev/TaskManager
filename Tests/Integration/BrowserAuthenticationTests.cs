using System.Net;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using System.Net.Sockets;
using BLL.Interfaces;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WebAPI.Authentication;
using WebAPI;
using WebAPI.Services;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests.Integration;

public class BrowserAuthenticationTests
{
    [Fact]
    public async Task CsrfBootstrap_ReturnsRequestToken()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.RequestToken));
    }

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    public async Task BrowserCredentialMutation_WithoutAntiforgery_IsRejected(string action)
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.PostAsJsonAsync($"/api/auth/{action}", new
        {
            userName = "new-member",
            firstName = "New",
            lastName = "Member",
            email = "new-member@example.test",
            password = "TestPassword123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutSession_ReturnsUnauthorizedWithoutRedirect()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task RegisterConfirmLoginMeLogout_UsesProtectedCookieAndOrdinaryAccount()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        await RefreshCsrfAsync(client);
        var registered = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "browser-user", firstName = "Олена", lastName = "Коваль",
            email = "browser-user@example.test", password = "TestPassword123!",
            teamId = 999, roles = new[] { "admin" }
        });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        var mail = Assert.Single(factory.Email.Messages);
        Assert.Equal("confirmation", mail.Kind);
        var confirmed = await client.PostAsJsonAsync("/api/auth/confirm-email", new
        {
            userId = mail.UserId, token = SmtpAccountEmailSender.EncodeToken(mail.Token)
        });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = mail.Email, password = "TestPassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-TaskManager.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", sessionCookie, StringComparison.OrdinalIgnoreCase);
        var profile = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(profile.TryGetProperty("token", out _));
        Assert.False(profile.TryGetProperty("teamId", out var team) && team.ValueKind != JsonValueKind.Null);
        Assert.Empty(profile.GetProperty("roles").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_UnknownWrongPasswordAndUnconfirmedAccount_ReturnSameError()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        await factory.CreateAccountAsync("confirmed@example.test", confirmed: true);
        await factory.CreateAccountAsync("unconfirmed@example.test", confirmed: false);
        await RefreshCsrfAsync(client);
        string? failure = null;
        foreach (var attempt in new[]
        {
            new { email = "missing@example.test", password = "TestPassword123!" },
            new { email = "confirmed@example.test", password = "WrongPassword123!" },
            new { email = "unconfirmed@example.test", password = "TestPassword123!" }
        })
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", attempt);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            // Correlation identifiers differ per request; credential information
            // must remain indistinguishable in the public error contract.
            var body = JsonSerializer.Serialize(new
            {
                status = problem.GetProperty("status").GetInt32(),
                title = problem.GetProperty("title").GetString()
            });
            failure ??= body;
            Assert.Equal(failure, body);
        }
    }

    [Fact]
    public async Task InvalidBearer_NeverFallsBackToExistingSessionCookie()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        await factory.CreateAccountAsync("member@example.test");
        await LoginAsync(client, "member@example.test");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task CookieLogout_WithoutValidCsrf_IsRejectedAndSessionRemainsActive(string? token)
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        await factory.CreateAccountAsync("member@example.test");
        await LoginAsync(client, "member@example.test");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (token is not null) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UsesEmailTokenRevokesOldSessionAndRejectsReplay()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        await factory.CreateAccountAsync("member@example.test");
        await LoginAsync(client, "member@example.test");
        await RefreshCsrfAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await manager.FindByEmailAsync("member@example.test");
            user!.AccessFailedCount = 5;
            Assert.True((await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(50))).Succeeded);
        }
        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "member@example.test" });
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "missing@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var mail = Assert.Single(factory.Email.Messages);
        Assert.Equal("reset", mail.Kind);
        var resetBody = new
        {
            email = mail.Email,
            token = SmtpAccountEmailSender.EncodeToken(mail.Token),
            newPassword = "ReplacementPassword123!"
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/reset-password", resetBody)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await manager.FindByEmailAsync("member@example.test");
            Assert.False(await manager.IsLockedOutAsync(user!));
            Assert.Equal(0, await manager.GetAccessFailedCountAsync(user!));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", resetBody)).StatusCode);
        await LoginAsync(client, mail.Email, "ReplacementPassword123!");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPasswordAndRevokesOtherSession()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var first = factory.CreateSecureClient();
        using var second = factory.CreateSecureClient();
        await factory.CreateAccountAsync("member@example.test");
        await LoginAsync(first, "member@example.test");
        await LoginAsync(second, "member@example.test");
        await RefreshCsrfAsync(first);
        var invalid = await first.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "wrong", newPassword = "ReplacementPassword123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var changed = await first.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "TestPassword123!", newPassword = "ReplacementPassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task SessionExpiry_Returns401WithoutRedirect()
    {
        await using var factory = new BrowserAuthenticationFactory(sessionMinutes: 5);
        using var client = factory.CreateSecureClient();
        await factory.CreateAccountAsync("member@example.test");
        await LoginAsync(client, "member@example.test");
        factory.Clock.Advance(TimeSpan.FromMinutes(6));

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task RepeatedBadPassword_TriggersIdentityLockout()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        await factory.CreateAccountAsync("member@example.test");
        await RefreshCsrfAsync(client);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = "member@example.test", password = "wrong"
            })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "member@example.test", password = "TestPassword123!"
        })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await manager.FindByEmailAsync("member@example.test");
        Assert.True(await manager.IsLockedOutAsync(user!));
    }

    [Fact]
    public async Task AccountRateLimit_Returns429AfterConfiguredLimit()
    {
        await using var factory = new BrowserAuthenticationFactory(authRequestsPerMinute: 5);
        using var client = factory.CreateSecureClient();
        await RefreshCsrfAsync(client);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = "missing@example.test", password = "wrong"
            })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "missing@example.test", password = "wrong"
        })).StatusCode);
    }

    [Fact]
    public async Task LegacyRegistration_SendsConfirmationAndVerifiedLoginReturnsJwt()
    {
        await using var factory = new BrowserAuthenticationFactory();
        using var client = factory.CreateSecureClient();
        var registered = await client.PostAsJsonAsync("/api/Users/Register", new
        {
            userName = "console-user", firstName = "Console", lastName = "User",
            email = "console-user@example.test", password = "TestPassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var mail = Assert.Single(factory.Email.Messages);
        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/confirm-email", new
        {
            userId = mail.UserId, token = SmtpAccountEmailSender.EncodeToken(mail.Token)
        })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/Users/Login", new { email = mail.Email, password = "TestPassword123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RegistrationSmtpLink_UsesConfiguredOriginDespiteHostHeader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var smtp = new LoopbackSmtpSink();
        var delivery = smtp.ReceiveAsync(timeout.Token);
        await using var factory = new BrowserAuthenticationFactory(
            publicOrigin: "https://taskmanager.example.test", smtpPort: smtp.Port, captureEmails: false);
        using var client = factory.CreateSecureClient();
        await RefreshCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new
            {
                userName = "smtp-user", firstName = "Олена", lastName = "Коваль",
                email = "smtp-user@example.test", password = "TestPassword123!"
            })
        };
        request.Headers.Host = "attacker.example";
        var response = await client.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var mime = await delivery;
        var mimeBody = mime[(mime.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        var text = mime.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(mimeBody))
            : mimeBody.Replace("=\r\n", string.Empty, StringComparison.Ordinal);
        Assert.Contains("https://taskmanager.example.test/confirm-email?", text);
        Assert.DoesNotContain("attacker.example", text);
    }

    [Fact]
    public async Task PersistentKeyRing_AfterHostRestartValidatesCookieAndResetTokenUntilStampChanges()
    {
        using var keyRing = new TemporaryAuthKeyRing();
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        string cookie;
        string resetToken;
        await using (var firstFactory = new BrowserAuthenticationFactory(existingDatabase: database, keyRingPath: keyRing.Path))
        {
            using var first = firstFactory.CreateSecureClient();
            await firstFactory.CreateAccountAsync("restart@example.test");
            await RefreshCsrfAsync(first);
            var login = await first.PostAsJsonAsync("/api/auth/login", new
            {
                email = "restart@example.test", password = "TestPassword123!"
            });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value =>
                value.StartsWith("__Host-TaskManager.Session=", StringComparison.Ordinal)).Split(';')[0];
            await RefreshCsrfAsync(first);
            Assert.Equal(HttpStatusCode.Accepted, (await first.PostAsJsonAsync("/api/auth/forgot-password", new
            {
                email = "restart@example.test"
            })).StatusCode);
            resetToken = SmtpAccountEmailSender.EncodeToken(Assert.Single(firstFactory.Email.Messages).Token);
        }

        await using var secondFactory = new BrowserAuthenticationFactory(existingDatabase: database, keyRingPath: keyRing.Path);
        using var second = secondFactory.CreateSecureClient();
        second.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);
        await RefreshCsrfAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = "restart@example.test", token = resetToken, newPassword = "RestartedPassword123!"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("not-an-origin")]
    [InlineData("https://user:password@taskmanager.example.test")]
    [InlineData("http://taskmanager.example.test")]
    public async Task ProductionPublicOrigin_InvalidFinalConfigurationPreventsStartup(string publicOrigin)
    {
        using var keyRing = new TemporaryAuthKeyRing();
        await using var factory = new BrowserAuthenticationFactory(
            publicOrigin: publicOrigin, keyRingPath: keyRing.Path, environmentName: "Production");

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateSecureClient());

        Assert.Contains("BrowserAuth:PublicOrigin", exception.Message);
    }

    [Fact]
    public async Task UnsupportedAzureKeyRingProvider_PreventsStartupInsteadOfFallingBack()
    {
        using var keyRing = new TemporaryAuthKeyRing();
        await using var factory = new BrowserAuthenticationFactory(
            publicOrigin: "https://taskmanager.example.test", keyRingPath: keyRing.Path,
            environmentName: "Production", dataProtectionProvider: "AzureBlob");

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateSecureClient());

        Assert.Contains("DataProtection:Provider is not implemented", exception.Message);
    }

    private static async Task LoginAsync(HttpClient client, string email, string password = "TestPassword123!")
    {
        await RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
    }

    private static async Task RefreshCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body!.RequestToken);
    }

    private sealed record CsrfResponse(string RequestToken);
}

public sealed class BrowserAuthenticationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    private readonly bool _ownsDatabase;
    private readonly int _authRequestsPerMinute;
    private readonly int _sessionMinutes;
    private readonly string _publicOrigin;
    private readonly int _smtpPort;
    private readonly bool _captureEmails;
    private readonly string? _keyRingPath;
    private readonly string _environmentName;
    private readonly string _dataProtectionProvider;
    public CapturingAccountEmailSender Email { get; } = new();
    public AdjustableAuthTimeProvider Clock { get; } = new();

    public BrowserAuthenticationFactory(int authRequestsPerMinute = 30, int sessionMinutes = 60,
        string publicOrigin = "https://localhost", int smtpPort = 1025, bool captureEmails = true,
        SqliteConnection? existingDatabase = null, string? keyRingPath = null,
        string environmentName = "Testing", string dataProtectionProvider = "FileSystem")
    {
        _authRequestsPerMinute = authRequestsPerMinute;
        _sessionMinutes = sessionMinutes;
        _publicOrigin = publicOrigin;
        _smtpPort = smtpPort;
        _captureEmails = captureEmails;
        _keyRingPath = keyRingPath;
        _environmentName = environmentName;
        _dataProtectionProvider = dataProtectionProvider;
        _ownsDatabase = existingDatabase is null;
        _connection = existingDatabase ?? new SqliteConnection("Data Source=:memory:");
        if (_connection.State != System.Data.ConnectionState.Open) _connection.Open();
    }

    public HttpClient CreateSecureClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TaskContext>().Database.EnsureCreated();
        return client;
    }

    public async System.Threading.Tasks.Task CreateAccountAsync(string email, bool confirmed = true)
    {
        using var scope = Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User(email, "Test", "Member", email) { EmailConfirmed = confirmed };
        var result = await manager.CreateAsync(user, "TestPassword123!");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Enabled"] = "false",
                ["Reminders:Enabled"] = "false",
                ["ConnectionStrings:DbConnection"] = "Server=fake;Database=fake;",
                ["Jwt:Key"] = "browser-authentication-test-signing-key",
                ["Jwt:Issuer"] = "TaskManagerAPI",
                ["Jwt:Audience"] = "TaskManagerClients",
                ["BrowserAuth:PublicOrigin"] = _publicOrigin,
                ["BrowserAuth:AuthRequestsPerMinute"] = _authRequestsPerMinute.ToString(),
                ["BrowserAuth:SessionMinutes"] = _sessionMinutes.ToString(),
                ["Email:SmtpHost"] = "127.0.0.1",
                ["Email:SmtpPort"] = _smtpPort.ToString(),
                ["Email:UseTls"] = (_environmentName == "Production").ToString(),
                ["Email:FromAddress"] = "noreply@taskmanager.example.test",
                ["DataProtection:KeyRingPath"] = _keyRingPath,
                ["DataProtection:Provider"] = _dataProtectionProvider
            }));
        builder.ConfigureServices(services =>
        {
            if (_keyRingPath is null) services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<TaskContext>();
            services.RemoveAll<DbContext>();
            services.RemoveAll<DbContextOptions<TaskContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<TaskContext>>();
            services.AddDbContext<TaskContext>(options => options.UseSqlite(_connection));
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<TaskContext>());
            if (_captureEmails)
            {
                services.RemoveAll<IAccountEmailSender>();
                services.AddSingleton<IAccountEmailSender>(Email);
            }
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = Clock);
            var backgroundServices = services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                (descriptor.ImplementationType == typeof(BootstrapAdminHostedService) ||
                 descriptor.ImplementationType == typeof(TaskReminderScheduler))).ToList();
            foreach (var descriptor in backgroundServices)
            {
                services.Remove(descriptor);
            }
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_ownsDatabase) await _connection.DisposeAsync();
    }
}

public sealed record CapturedAccountEmail(string Kind, string Email, string? UserId, string Token);

public sealed class CapturingAccountEmailSender : IAccountEmailSender
{
    public ConcurrentQueue<CapturedAccountEmail> Messages { get; } = new();
    public System.Threading.Tasks.Task SendConfirmationAsync(string email, string userId, string token, CancellationToken ct = default)
    {
        Messages.Enqueue(new CapturedAccountEmail("confirmation", email, userId, token));
        return System.Threading.Tasks.Task.CompletedTask;
    }
    public System.Threading.Tasks.Task SendPasswordResetAsync(string email, string token, CancellationToken ct = default)
    {
        Messages.Enqueue(new CapturedAccountEmail("reset", email, null, token));
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

public sealed class AdjustableAuthTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>One local SMTP exchange; no messages leave the test process.</summary>
internal sealed class LoopbackSmtpSink : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public LoopbackSmtpSink() => _listener.Start();

    public async System.Threading.Tasks.Task<string> ReceiveAsync(CancellationToken token)
    {
        using var connection = await _listener.AcceptTcpClientAsync(token);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true)
        {
            NewLine = "\r\n", AutoFlush = true
        };
        await writer.WriteLineAsync("220 localhost test SMTP ready");
        while (await reader.ReadLineAsync(token) is { } command)
        {
            if (command.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                command.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 localhost");
            }
            else if (command.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) ||
                     command.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 OK");
            }
            else if (command.Equals("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 End data with a dot");
                var message = new StringBuilder();
                while (await reader.ReadLineAsync(token) is { } line && line != ".")
                {
                    message.Append(line).Append("\r\n");
                }
                await writer.WriteLineAsync("250 Message accepted");
                return message.ToString();
            }
            else
            {
                await writer.WriteLineAsync("500 Unsupported test SMTP command");
            }
        }
        throw new IOException("SMTP connection ended before message delivery.");
    }

    public void Dispose() => _listener.Stop();
}

internal sealed class TemporaryAuthKeyRing : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "taskmanager-auth-" + Guid.NewGuid().ToString("N"));
    public TemporaryAuthKeyRing() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        // This directory is created by this test and contains only key XML files.
        foreach (var file in Directory.EnumerateFiles(Path)) File.Delete(file);
        Directory.Delete(Path);
    }
}
