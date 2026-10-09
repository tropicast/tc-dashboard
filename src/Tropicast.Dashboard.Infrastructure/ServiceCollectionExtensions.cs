using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tropicast.Dashboard.Application;

namespace Tropicast.Dashboard.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the adapters for the Application ports.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
