using System.Text.RegularExpressions;

namespace Tropicast.Dashboard.Api.Logging;

/// <summary>
/// Last line of defence for log output: masks anything shaped like a credential. The code never logs secrets on
/// purpose (SecretLoggingTests); this catches what slips through, e.g. in an exception message.
/// </summary>
public static partial class Redaction
{
    internal const string Mask = "[redacted]";

    /// <summary>The text with credentials masked.</summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        text = AuthorizationHeader().Replace(text, m => $"{m.Groups["scheme"].Value} {Mask}");
        text = UrlCredentials().Replace(text, $"://{Mask}@");
        text = KeyValueSecret().Replace(text, m => $"{m.Groups["key"].Value}{m.Groups["sep"].Value}{Mask}");
        text = JsonSecret().Replace(text, m => $"{m.Groups["key"].Value}{Mask}\"");
        text = DeviceToken().Replace(text, Mask);
        return Base64UrlSecret().Replace(text, Mask);
    }

    /// <summary><c>Bearer …</c>, <c>Basic …</c>.</summary>
    [GeneratedRegex(@"\b(?<scheme>Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeader();

    /// <summary><c>scheme://user:password@host</c>.</summary>
    [GeneratedRegex(@"://[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlCredentials();

    /// <summary>
    /// <c>password=…</c>, <c>Password: …</c>, <c>?code=…</c>, <c>token=…</c> (query strings, connection strings), with
    /// the value bare or quoted (<c>Password="a;b"</c>, <c>'…'</c>, with <c>""</c> or backslash escapes).
    /// </summary>
    [GeneratedRegex(@"(?<key>\b(?:password|passwd|pwd|secret|token|refresh_?token|access_?token|code|pass|api[_-]?key)\b)(?<sep>\s*[=:]\s*)(?!\[redacted\])(?:""(?:[^""\\]|\\.|"""")*""?|'(?:[^'\\]|\\.|'')*'?|[^&\s;,""']+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecret();

    /// <summary><c>"password":"…"</c> and similar JSON members.</summary>
    [GeneratedRegex(@"(?<key>""(?:password|newPassword|currentPassword|secret|refreshToken|accessToken|deviceCode|code)""\s*:\s*"")(?:[^""\\]|\\.)*""",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();

    /// <summary>Refresh tokens and device codes: <c>{32 hex}.{secret}</c>.</summary>
    [GeneratedRegex(@"\b[0-9a-f]{32}\.[A-Za-z0-9_-]{20,}")]
    private static partial Regex DeviceToken();

    /// <summary>256-bit secrets as issued (43 base64url characters): broadcast passwords, token secrets.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])")]
    private static partial Regex Base64UrlSecret();
}
