using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tropicast.Dashboard.Application;

namespace Tropicast.Dashboard.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=tropicast;Username=tropicast";
        var options = new DbContextOptionsBuilder<AppDbContext>();
        PersistenceSetup.Configure(options, connectionString);
        return new AppDbContext(options.Options, new NoTenant());
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
    }
}
