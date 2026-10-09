using Microsoft.AspNetCore.WebUtilities;
using Tropicast.Dashboard.Application.Email;

namespace Tropicast.Dashboard.Api.Auth;

/// <summary>Email texts. Links carry one-time codes: they go to the recipient only, never to logs.</summary>
internal static class AuthEmails
{
    internal static EmailMessage ConfirmEmail(string to, string baseUrl, Guid userId, string code) => new(to,
        "Confirm your Tropicast account",
        $"""
        Welcome to Tropicast.

        Confirm your email address to start broadcasting:
        {Link(baseUrl, "confirm-email", ("userId", userId.ToString()), ("code", Encode(code)))}

        If you did not sign up, ignore this email.
        """);

    internal static EmailMessage AccountExists(string to, string baseUrl) => new(to,
        "You already have a Tropicast account",
        $"""
        Someone tried to sign up with this email address, which already has an account.

        Sign in: {baseUrl.TrimEnd('/')}/sign-in
        Forgot your password? {baseUrl.TrimEnd('/')}/forgot-password

        If this was not you, ignore this email.
        """);

    internal static EmailMessage ResetPassword(string to, string baseUrl, string code) => new(to,
        "Reset your Tropicast password",
        $"""
        Choose a new password with this link:
        {Link(baseUrl, "reset-password", ("email", to), ("code", Encode(code)))}

        Signed-in desktop devices will have to sign in again. If you did not ask for this, ignore this email.
        """);

    internal static EmailMessage Invitation(string to, string baseUrl, string tenantName, string role, string token) => new(to,
        $"You are invited to {tenantName} on Tropicast",
        $"""
        You are invited to join {tenantName} on Tropicast as {role}.

        Accept the invitation (sign up first with this email address if needed):
        {Link(baseUrl, "invitations/accept", ("token", token))}

        The link is valid for 7 days.
        """);

    /// <summary>Identity codes are base64 with characters that are not URL-safe.</summary>
    internal static string Encode(string code) => WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));

    internal static string? Decode(string code)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Link(string baseUrl, string path, params (string Key, string Value)[] query)
        => QueryHelpers.AddQueryString($"{baseUrl.TrimEnd('/')}/{path}", query.Select(q => KeyValuePair.Create(q.Key, (string?)q.Value)));
}
