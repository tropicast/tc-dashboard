using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Application.Email;
using Tropicast.Dashboard.Infrastructure.Email;
using Tropicast.Dashboard.Application.Outbox;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Outbox;
using Tropicast.Dashboard.Infrastructure.Provisioning;
using Tropicast.Dashboard.Infrastructure.Stats;
using Tropicast.Dashboard.Application.Stats;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the adapters for the Application ports.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<CurrentTenant>();
        services.TryAddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
        services.Configure<ProvisioningOptions>(configuration.GetSection("Provisioning"));
        services.TryAddSingleton<IStreamingNodeClient, SshStreamingNodeClient>();
        services.AddSingleton<ProvisioningSignal>();
        services.AddScoped<Reconciler>();
        services.AddSingleton<IOutboxConsumer, ProvisioningOutboxConsumer>();
        services.AddHostedService<ProvisioningWorker>();
        services.AddHostedService<DeviceAuthorizationCleanup>();
        services.Configure<StatsOptions>(configuration.GetSection("Stats"));
        services.AddSingleton<LiveStatusStore>();
        services.TryAddSingleton<IStreamingStatsClient, HttpStreamingStatsClient>();
        services.AddScoped<StatsRecorder>();
        services.AddSingleton<StatsCollector>();
        services.AddHostedService(sp => sp.GetRequiredService<StatsCollector>());
        // Read when a context is created, so hosts without a database (e.g. OpenAPI generation) still start.
        services.AddDbContext<AppDbContext>(options => PersistenceSetup.Configure(options,
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set the connection string ConnectionStrings:Default.")));
        return services;
    }

    /// <summary>
    /// Applies pending migrations. For local development only: production runs the migration bundle at deploy.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
