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
        => Slugs.IsValid(value) ? value : throw new ArgumentException(Slugs.Rule, name);

    internal static string Sha256Hex(string value, string name)
        => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower) ? value
            : throw new ArgumentException("Expected a lowercase hex SHA-256 hash.", name);

}

/// <summary>URL slugs for tenants and stations.</summary>
public static partial class Slugs
{
    public const string Rule = "Use 3-64 lowercase letters, digits and single hyphens, starting and ending with a letter or digit.";

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){2,63}$")]
    private static partial Regex Pattern();
}
