using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Tropicast.Dashboard.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers use cases and every validator in this assembly.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly, includeInternalTypes: true);
        return services;
    }
}
