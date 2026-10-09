using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Tropicast.Dashboard.Domain.Stations;

/// <summary>
/// The station ID in listener URLs, Icecast mounts and source-auth usernames:
/// <c>/stations/{id}/live.mp3</c>. Ten characters of lowercase Crockford base32 (50 bits), never reused or changed.
/// </summary>
public static partial class StationPublicId
{
    public const int Length = 10;
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^[0-9a-hjkmnp-tv-z]{10}$")]
    private static partial Regex Pattern();
}
