using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Tropicast.Dashboard.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(Tropicast.Dashboard.Infrastructure.Tests.PostgresFixture))]

namespace Tropicast.Dashboard.Infrastructure.Tests;

/// <summary>One PostgreSQL 17 container per test run, migrated from an empty database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext(null);
        await context.Database.MigrateAsync();
    }

    /// <summary>A context scoped to <paramref name="tenantId"/>, as the API creates one per request.</summary>
    public AppDbContext CreateContext(Guid? tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        PersistenceSetup.Configure(options, ConnectionString);
        return new AppDbContext(options.Options, new CurrentTenant { TenantId = tenantId });
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
