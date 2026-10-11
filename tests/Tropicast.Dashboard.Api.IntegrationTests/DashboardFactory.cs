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
    public FakeStatsClient Stats { get; } = new();
    public FakeRadioDirectory Directory { get; } = new();
    public FakePaymentProvider Payments { get; } = new();
    /// <summary>Extra configuration for one factory (e.g. enabling the background loops).</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
    public CapturingLoggerProvider Logs { get; } = new();
    /// <summary>Per-IP limit for the credential endpoints; high so tests do not trip it.</summary>
    public int AuthPermitLimit { get; init; } = 10_000;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        // Migrate before the host starts, as production does (migration bundle, then the API): Data Protection
        // reads its key ring from the database at startup.
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Tropicast.Dashboard.Infrastructure.Persistence.AppDbContext>();
        Tropicast.Dashboard.Infrastructure.Persistence.PersistenceSetup.ConfigureStandalone(options, _connectionString);
        await using var db = new Tropicast.Dashboard.Infrastructure.Persistence.AppDbContext(options.Options, new NoTenant());
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database);
        // Then start the host, so startup errors surface here.
        _ = Services;
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
        // Billing on, with a stand-in provider (FakePaymentProvider); prices for the paid plans.
        builder.UseSetting("Billing:SecretKey", "sk_test_fake");
        builder.UseSetting("Billing:Prices:starter", "price_starter");
        builder.UseSetting("Billing:Prices:growth", "price_growth");
        builder.UseSetting("Billing:DisplayPrices:starter", "€9 / month");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.UseSetting("SourceAuth:NodeUsername", SourceAuthNode.Username);
        builder.UseSetting("SourceAuth:NodePassword", SourceAuthNode.Password);
        builder.UseSetting("RateLimits:Auth:PermitLimit", AuthPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var limit in new[] { "PublicPermitLimit", "DevicePollPermitLimit", "SourceAuthPermitLimit" })
        {
            builder.UseSetting($"RateLimits:{limit}", Settings.GetValueOrDefault($"RateLimits:{limit}") ?? "100000");
        }
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, LocalPortFilter>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<Tropicast.Dashboard.Application.Provisioning.IStreamingNodeClient>();
            services.AddSingleton<Tropicast.Dashboard.Application.Provisioning.IStreamingNodeClient>(Node);
            services.RemoveAll<Tropicast.Dashboard.Application.Stats.IStreamingStatsClient>();
            services.AddSingleton<Tropicast.Dashboard.Application.Stats.IStreamingStatsClient>(Stats);
            services.RemoveAll<Tropicast.Dashboard.Application.Directories.IRadioDirectory>();
            services.AddSingleton<Tropicast.Dashboard.Application.Directories.IRadioDirectory>(Directory);
            services.RemoveAll<Tropicast.Dashboard.Application.Billing.IPaymentProvider>();
            services.AddSingleton<Tropicast.Dashboard.Application.Billing.IPaymentProvider>(Payments);
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

/// <summary>
/// Payment provider stand-in: checkout and portal URLs it records; webhooks are a JSON <see cref="Application.Billing.BillingEvent"/>
/// signed "valid" (Stripe's own parsing and signatures are covered in Infrastructure tests).
/// </summary>
public sealed class FakePaymentProvider : Tropicast.Dashboard.Application.Billing.IPaymentProvider
{
    public static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public ConcurrentQueue<Tropicast.Dashboard.Application.Billing.CheckoutRequest> Checkouts { get; } = new();

    public bool Sells(string planId) => planId is "starter" or "growth";

    public Task<Uri> CreateCheckoutAsync(Tropicast.Dashboard.Application.Billing.CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        Checkouts.Enqueue(request);
        return Task.FromResult(new Uri($"https://checkout.stripe.test/c/{request.TenantId}"));
    }

    public Task<Uri> CreatePortalAsync(string customerId, Uri returnUrl, CancellationToken cancellationToken = default)
        => Task.FromResult(new Uri($"https://billing.stripe.test/p/{customerId}"));

    public Tropicast.Dashboard.Application.Billing.BillingEvent ParseWebhook(string payload, string signature)
        => signature == "valid"
            ? System.Text.Json.JsonSerializer.Deserialize<Tropicast.Dashboard.Application.Billing.BillingEvent>(payload, Json)!
            : throw new Tropicast.Dashboard.Application.Billing.BillingException("The webhook signature is invalid or too old.");
}

/// <summary>RadioBrowser stand-in: records additions, or fails like an outage.</summary>
public sealed class FakeRadioDirectory : Tropicast.Dashboard.Application.Directories.IRadioDirectory
{
    private readonly ConcurrentQueue<Tropicast.Dashboard.Application.Directories.DirectoryEntry> _added = new();

    public IReadOnlyList<Tropicast.Dashboard.Application.Directories.DirectoryEntry> Added => [.. _added];
    public bool Fail { get; set; }

    public Task<string> AddAsync(Tropicast.Dashboard.Application.Directories.DirectoryEntry entry, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new Tropicast.Dashboard.Application.Directories.DirectoryException("RadioBrowser is unreachable. The listing is retried later.");
        }
        _added.Enqueue(entry);
        var uuid = Guid.NewGuid().ToString();
        _entries[entry.StreamUrl] = uuid;
        return Task.FromResult(uuid);
    }

    private readonly ConcurrentDictionary<Uri, string> _entries = new();

    /// <summary>Entries RadioBrowser already has, by stream URL (including ones added outside a test's view).</summary>
    public void Has(Uri streamUrl, string uuid) => _entries[streamUrl] = uuid;

    public Task<string?> FindAsync(Uri streamUrl, CancellationToken cancellationToken = default)
        => Fail
            ? throw new Tropicast.Dashboard.Application.Directories.DirectoryException("RadioBrowser is unreachable. The listing is retried later.")
            : Task.FromResult(_entries.GetValueOrDefault(streamUrl));
}

internal sealed class NoTenant : Tropicast.Dashboard.Application.ICurrentTenant
{
    public Guid? TenantId => null;
}

/// <summary>Returns the sample a test sets, as the node's exporter would.</summary>
public sealed class FakeStatsClient : Tropicast.Dashboard.Application.Stats.IStreamingStatsClient
{
    public Tropicast.Dashboard.Application.Stats.NodeSample? Next { get; set; }

    public Task<Tropicast.Dashboard.Application.Stats.NodeSample> GetAsync(string node, CancellationToken cancellationToken = default)
        => Next is { } sample ? Task.FromResult(sample) : throw new HttpRequestException("Node unreachable.");
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
