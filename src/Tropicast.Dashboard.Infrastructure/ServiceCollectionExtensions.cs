using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Application.Email;
using Tropicast.Dashboard.Infrastructure.Email;
using Tropicast.Dashboard.Infrastructure.Outbox;
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
