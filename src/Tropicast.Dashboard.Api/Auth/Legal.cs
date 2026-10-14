namespace Tropicast.Dashboard.Api.Auth;

/// <summary>The legal documents users accept: the Terms of Service and the Privacy Policy (#94).</summary>
internal static class Legal
{
    /// <summary>
    /// Version of the texts in <c>web/src/legal/</c>, as in <c>web/src/legal/legal.json</c> (a test keeps them equal).
    /// Change both when the texts change: every user is then asked to accept the new version.
    /// </summary>
    internal const string TermsVersion = "2026-10-14";
}
