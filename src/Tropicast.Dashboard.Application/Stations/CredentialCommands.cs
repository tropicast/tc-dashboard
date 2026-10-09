using FluentValidation;

namespace Tropicast.Dashboard.Application.Stations;

/// <summary>Issues a broadcast credential for one device of a station.</summary>
/// <param name="DeviceLabel">Names the device, e.g. "Studio PC"; unique among the station's active credentials.</param>
public sealed record CreateCredentialCommand(string DeviceLabel);

internal sealed class CreateCredentialValidator : AbstractValidator<CreateCredentialCommand>
{
    public CreateCredentialValidator()
        => RuleFor(c => c.DeviceLabel).NotEmpty().MaximumLength(64)
            .Must(label => label is null || !label.Any(char.IsControl)).WithMessage("No control characters.");
}
