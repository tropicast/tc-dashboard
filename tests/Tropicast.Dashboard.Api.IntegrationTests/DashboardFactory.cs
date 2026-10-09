using System.Collections.Concurrent;
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
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
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
        builder.UseSetting("RateLimits:Auth:PermitLimit", AuthPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
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
