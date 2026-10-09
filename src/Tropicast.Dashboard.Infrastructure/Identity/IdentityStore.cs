using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Tropicast.Dashboard.Infrastructure.Identity;

/// <summary>
/// Identity settings that shape the database schema. The Identity model reads them from the application services,
/// so the API, `dotnet ef` and tests must all use these, or their models (and migrations) differ.
/// </summary>
public static class IdentityStore
{
    public static void Configure(StoreOptions stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        // Schema without passkeys (not used in the MVP).
        stores.SchemaVersion = IdentitySchemaVersions.Version2;
        stores.MaxLengthForKeys = 128;
    }

    /// <summary>Application services carrying only these settings, for contexts created outside the API host.</summary>
    internal static IServiceProvider Services { get; } = new ServiceCollection()
        .Configure<IdentityOptions>(options => Configure(options.Stores))
        .BuildServiceProvider();
}
