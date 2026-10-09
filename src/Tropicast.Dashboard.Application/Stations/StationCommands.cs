using FluentValidation;
using Tropicast.Dashboard.Domain;

namespace Tropicast.Dashboard.Application.Stations;

/// <summary>Creates a station in the current tenant.</summary>
/// <param name="Name">Display name, e.g. "Radio Mada".</param>
/// <param name="Slug">URL name, unique in the tenant, e.g. "radio-mada".</param>
/// <param name="Description">Shown on the public page and in directories.</param>
/// <param name="Genre">e.g. "Talk" or "Salegy".</param>
/// <param name="Country">ISO 3166-1 alpha-2, e.g. "MG".</param>
/// <param name="Language">BCP 47 tag, e.g. "mg" or "fr".</param>
/// <param name="LogoUrl">HTTPS address of a square logo.</param>
/// <param name="Website">The station's website.</param>
/// <param name="ListInDirectory">List on RadioBrowser, if the plan includes it.</param>
public sealed record CreateStationCommand(string Name, string Slug, string? Description = null, string? Genre = null,
    string? Country = null, string? Language = null, Uri? LogoUrl = null, Uri? Website = null, bool ListInDirectory = false);

/// <summary>Changes station settings; omitted (null) fields keep their value.</summary>
public sealed record UpdateStationCommand(string? Name = null, string? Slug = null, string? Description = null, string? Genre = null,
    string? Country = null, string? Language = null, Uri? LogoUrl = null, Uri? Website = null, bool? ListInDirectory = null);

internal static class StationRules
{
    internal static bool IsCountry(string? value) => value is { Length: 2 } && value.All(char.IsAsciiLetter);

    /// <summary>A pragmatic BCP 47 check: language, then optional subtags.</summary>
    internal static bool IsLanguage(string? value)
        => value is { Length: <= 35 } && System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$");

    internal static bool IsWebAddress(Uri? value) => value is { IsAbsoluteUri: true } && value.Scheme is "https" or "http";
}

internal sealed class CreateStationValidator : AbstractValidator<CreateStationCommand>
{
    public CreateStationValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Slug).NotEmpty().Must(Slugs.IsValid).WithMessage(Slugs.Rule);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.Genre).MaximumLength(64);
        RuleFor(c => c.Country).Must(StationRules.IsCountry).WithMessage("Use a two-letter country code, e.g. MG.").When(c => !string.IsNullOrEmpty(c.Country));
        RuleFor(c => c.Language).Must(StationRules.IsLanguage).WithMessage("Use a language tag, e.g. mg or fr.").When(c => !string.IsNullOrEmpty(c.Language));
        RuleFor(c => c.LogoUrl).Must(StationRules.IsWebAddress).WithMessage("Use an absolute http(s) address.").When(c => c.LogoUrl is not null);
        RuleFor(c => c.Website).Must(StationRules.IsWebAddress).WithMessage("Use an absolute http(s) address.").When(c => c.Website is not null);
    }
}

internal sealed class UpdateStationValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100).When(c => c.Name is not null);
        RuleFor(c => c.Slug!).Must(Slugs.IsValid).WithMessage(Slugs.Rule).When(c => c.Slug is not null);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.Genre).MaximumLength(64);
        RuleFor(c => c.Country).Must(StationRules.IsCountry).WithMessage("Use a two-letter country code, e.g. MG.").When(c => !string.IsNullOrEmpty(c.Country));
        RuleFor(c => c.Language).Must(StationRules.IsLanguage).WithMessage("Use a language tag, e.g. mg or fr.").When(c => !string.IsNullOrEmpty(c.Language));
        RuleFor(c => c.LogoUrl).Must(StationRules.IsWebAddress).WithMessage("Use an absolute http(s) address.").When(c => c.LogoUrl is not null);
        RuleFor(c => c.Website).Must(StationRules.IsWebAddress).WithMessage("Use an absolute http(s) address.").When(c => c.Website is not null);
    }
}
