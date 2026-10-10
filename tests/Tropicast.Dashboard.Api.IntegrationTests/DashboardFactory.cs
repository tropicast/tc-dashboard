using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Tropicast.Dashboard.Application.Email;
using Tropicast.Dashboard.Infrastructure;

[assembly: AssemblyFixture(typeof(Tropicast.Dashboard.Api.IntegrationTests.PostgresContainer))]

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>One PostgreSQL 17 container per test run; each factory gets its own database.</summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("mirror.gcr.io/library/postgres:17-alpine").Build();
    private int _databases;

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Interlocked.Increment(ref _databases)}";
        await _container.ExecScriptAsync($"CREATE DATABASE {name};");
        return new Npgsql.NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

/// <summary>The API against a migrated database, with captured email and logs.</summary>
public sealed class DashboardFactory(PostgresContainer postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _connectionString = "";

    public CapturingEmailSender Emails { get; } = new();
    public FakeNodeClient Node { get; } = new();
    /// <summary>Extra configuration for one factory (e.g. enabling the background loops).</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
    public CapturingLoggerProvider Logs { get; } = new();
    /// <summary>Per-IP limit for the credential endpoints; high so tests do not trip it.</summary>
    public int AuthPermitLimit { get; init; } = 10_000;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        await Services.MigrateDatabaseAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("App:PublicBaseUrl", "https://app.test");
        // Tests read outbox rows and drive the dispatcher themselves.
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Provisioning:Enabled", "false");
        // Tests share one database per factory: lift the node limits except where a test sets its own.
        builder.UseSetting("Streaming:MaxSources", "100000");
        builder.UseSetting("Streaming:MaxListenerCaps", "100000000");
        builder.UseSetting("Operators:Emails:0", "operator@example.test");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.UseSetting("SourceAuth:NodeUsername", SourceAuthNode.Username);
        builder.UseSetting("SourceAuth:NodePassword", SourceAuthNode.Password);
        builder.UseSetting("RateLimits:Auth:PermitLimit", AuthPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, LocalPortFilter>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<Tropicast.Dashboard.Application.Provisioning.IStreamingNodeClient>();
            services.AddSingleton<Tropicast.Dashboard.Application.Provisioning.IStreamingNodeClient>(Node);
        });
    }

    /// <summary>A browser-like client: HTTPS (so Secure cookies flow), cookies kept, no redirects.</summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Node credentials the test Icecast "sends".</summary>
public static class SourceAuthNode
{
    public const string Username = "icecast";
    public const string Password = "node-secret-for-tests";
}

/// <summary>Records what would be applied to the streaming node; can be made to fail.</summary>
public sealed class FakeNodeClient : Tropicast.Dashboard.Application.Provisioning.IStreamingNodeClient
{
    private readonly ConcurrentQueue<string> _applied = new();

    public IReadOnlyList<string> Applied => [.. _applied];
    public bool Fail { get; set; }

    public Task ApplyStationsAsync(string node, string stationsJson, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new IOException("Connection refused by tc-stream-1:22");
        }
        _applied.Enqueue(stationsJson);
        return Task.CompletedTask;
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage Last(string to) => Sent.Last(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

/// <summary>
/// TestServer has no sockets: set the connection's local port like Kestrel would. The listener is the Host header's
/// port unless X-Test-Local-Port says the request really arrived elsewhere (to test a spoofed Host).
/// </summary>
internal sealed class LocalPortFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
        => app =>
        {
            app.Use((Microsoft.AspNetCore.Http.HttpContext context, Func<Task> nextMiddleware) =>
            {
                context.Connection.LocalPort = int.TryParse(context.Request.Headers["X-Test-Local-Port"], out var port)
                    ? port : context.Request.Host.Port ?? 80;
                return nextMiddleware();
            });
            next(app);
        };
}
