namespace Tropicast.Dashboard.Domain;

/// <summary>A business rule violation. <see cref="Code"/> is stable and machine-readable; the API maps it to Problem Details.</summary>
public sealed record DomainError
{
    public DomainError(string code, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Code = code;
        Description = description;
    }

    public string Code { get; }
    public string Description { get; }
}
