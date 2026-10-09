using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tropicast.Dashboard.Infrastructure.Persistence;

internal static class PersistenceSetup
{
    internal static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        // No Kerberos: skip the GSS probe, which needs libgssapi (absent from the runtime image).
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable };
        options
            .UseNpgsql(builder.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
    }
}
