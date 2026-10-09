using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Tropicast.Dashboard.Application.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void Every_validator_in_the_application_assembly_is_registered()
    {
        var validators = typeof(IClock).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IValidator).IsAssignableFrom(t))
            .ToList();
        var services = new ServiceCollection().AddApplication();
        Assert.All(validators, v => Assert.Contains(services, d => d.ImplementationType == v));
    }
}
