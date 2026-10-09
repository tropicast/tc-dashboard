using System.Reflection;
using NetArchTest.Rules;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Architecture.Tests;

/// <summary>Dependencies point inward: Domain ← Application ← Infrastructure ← Api.</summary>
public sealed class DependencyRuleTests
{
    private const string Ns = "Tropicast.Dashboard";
    private static readonly Assembly DomainAssembly = typeof(DomainError).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IClock).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.ServiceCollectionExtensions).Assembly;

    // Framework namespaces the inner layers must not use. EF Core lives only in Infrastructure.
    private static readonly string[] Frameworks = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"];

    public static TheoryData<string, string> Rules => new()
    {
        { "Domain", $"{Ns}.Application" },
        { "Domain", $"{Ns}.Infrastructure" },
        { "Domain", $"{Ns}.Api" },
        { "Domain", "Microsoft.Extensions" },
        { "Application", $"{Ns}.Infrastructure" },
        { "Application", $"{Ns}.Api" },
        { "Infrastructure", $"{Ns}.Api" },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Layer_does_not_depend_on_an_outer_layer(string layer, string forbidden)
        => AssertNoDependency(Layer(layer), forbidden);

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    public void Inner_layers_do_not_use_web_or_database_frameworks(string layer)
    {
        foreach (var framework in Frameworks)
        {
            AssertNoDependency(Layer(layer), framework);
            // Also catches a framework referenced only through attributes or inheritance metadata.
            Assert.DoesNotContain(Layer(layer).GetReferencedAssemblies(),
                a => a.Name!.StartsWith(framework, StringComparison.Ordinal));
        }
    }

    private static Assembly Layer(string name) => name switch
    {
        "Domain" => DomainAssembly,
        "Application" => ApplicationAssembly,
        "Infrastructure" => InfrastructureAssembly,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static void AssertNoDependency(Assembly assembly, string forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(forbidden).GetResult();
        Assert.True(result.IsSuccessful,
            $"{assembly.GetName().Name} depends on {forbidden}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
