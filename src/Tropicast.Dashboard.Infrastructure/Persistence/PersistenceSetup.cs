using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tropicast.Dashboard.Infrastructure.Identity;

namespace Tropicast.Dashboard.Infrastructure.Persistence;

internal static class PersistenceSetup
{
    /// <summary>Configures a context created outside the API host (dotnet ef, tests), with the same Identity settings.</summary>
    internal static void ConfigureStandalone(DbContextOptionsBuilder options, string connectionString)
    {
        Configure(options, connectionString);
        options.UseApplicationServiceProvider(IdentityStore.Services);
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        // No Kerberos: skip the GSS probe, which needs libgssapi (absent from the runtime image).
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable };
        options
            .UseNpgsql(builder.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
    }
}
