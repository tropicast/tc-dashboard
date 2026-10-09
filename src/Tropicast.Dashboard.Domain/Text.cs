using System.Text.RegularExpressions;

namespace Tropicast.Dashboard.Domain;

/// <summary>Guards for text fields. Input validation with user-facing messages happens in Application.</summary>
internal static partial class Text
{
    internal static string Required(string value, int maxLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"At most {maxLength} characters.");
    }

    internal static string Optional(string? value, int maxLength, string name)
        => string.IsNullOrWhiteSpace(value) ? "" : Required(value, maxLength, name);

    internal static string Slug(string value, string name)
        => SlugPattern().IsMatch(value) ? value
            : throw new ArgumentException("Use 3-64 lowercase letters, digits and single hyphens.", name);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){2,63}$")]
    private static partial Regex SlugPattern();
}
