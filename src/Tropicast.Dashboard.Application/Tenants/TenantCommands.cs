using FluentValidation;
using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Application.Tenants;

/// <summary>Creates a tenant; the caller becomes its Owner.</summary>
/// <param name="Name">Display name, e.g. "Radio Mada Group".</param>
/// <param name="Slug">URL name, unique across Tropicast, e.g. "radio-mada".</param>
public sealed record CreateTenantCommand(string Name, string Slug);

/// <summary>Changes the tenant's name or slug; omitted fields keep their value.</summary>
public sealed record UpdateTenantCommand(string? Name, string? Slug);

internal sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Slug).NotEmpty().Must(Slugs.IsValid).WithMessage(Slugs.Rule);
    }
}

internal sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100).When(c => c.Name is not null);
        RuleFor(c => c.Slug!).Must(Slugs.IsValid).WithMessage(Slugs.Rule).When(c => c.Slug is not null);
    }
}
