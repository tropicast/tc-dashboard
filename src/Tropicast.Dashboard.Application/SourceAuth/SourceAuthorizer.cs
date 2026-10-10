using System.Globalization;
using System.Text.RegularExpressions;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.SourceAuth;

/// <summary>What Icecast sends when a source connects (tc-streaming docs/source-auth.md), already form-decoded.</summary>
/// <param name="Mount">Requested mount, e.g. <c>/stations/k3m9x2p7qa/live.mp3</c>.</param>
/// <param name="User">Source username: must be the station's public ID.</param>
/// <param name="Password">Station broadcast credential secret.</param>
/// <param name="IceBitrate">The <c>header.ice-bitrate</c> field, if sent.</param>
/// <param name="IceAudioInfo">The <c>header.ice-audio-info</c> field, if sent.</param>
public sealed record SourceAuthRequest(string? Mount, string? User, string? Password, string? IceBitrate, string? IceAudioInfo);

/// <summary>The station data a decision needs. Null when the station does not exist or was deleted.</summary>
/// <param name="PublicId">The station's public ID.</param>
/// <param name="TenantActive">False when the tenant is suspended.</param>
/// <param name="Plan">The tenant's plan.</param>
/// <param name="ActiveCredentials">Active (unrevoked) credentials: ID and SHA-256 hash of the secret.</param>
public sealed record SourceAuthStation(string PublicId, bool TenantActive, Plan Plan,
    IReadOnlyList<(Guid Id, string SecretHash)> ActiveCredentials);

/// <summary>Allow with the matching credential, or deny with a reason safe to log (never the password).</summary>
public sealed record SourceAuthDecision(bool Allowed, string Reason, Guid? CredentialId = null, AudioFormat? Format = null)
{
    public static SourceAuthDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// The source-auth rules: the mount is <c>/stations/{id}/live.(mp3|opus)</c>, the user is that station, the password
/// matches an active credential (constant time), the plan allows the format, and a declared bitrate is within the plan.
/// </summary>
public static partial class SourceAuthorizer
{
    /// <summary>Same mount layout the gateway and Icecast accept.</summary>
    [GeneratedRegex(@"^/stations/(?<station>[A-Za-z0-9-]{1,64})/live\.(?<format>mp3|opus)$")]
    private static partial Regex MountPattern();

    [GeneratedRegex(@"(?:^|;)\s*(?:ice-)?bitrate=(\d+)")]
    private static partial Regex AudioInfoBitrate();

    /// <summary>The station a mount names, or null when the mount is outside the layout.</summary>
    public static string? StationOf(string? mount)
        => mount is not null && MountPattern().Match(mount) is { Success: true } match ? match.Groups["station"].Value : null;

    public static SourceAuthDecision Decide(SourceAuthRequest request, SourceAuthStation? station)
    {
        ArgumentNullException.ThrowIfNull(request);
        var match = MountPattern().Match(request.Mount ?? "");
        if (!match.Success)
        {
            return SourceAuthDecision.Deny("mount outside /stations/{id}/live.(mp3|opus)");
        }
        if (station is null || !string.Equals(request.User, match.Groups["station"].Value, StringComparison.Ordinal)
            || !string.Equals(station.PublicId, request.User, StringComparison.Ordinal))
        {
            return SourceAuthDecision.Deny("unknown station or wrong user");
        }
        if (!station.TenantActive)
        {
            return SourceAuthDecision.Deny("tenant suspended");
        }
        // Check every active credential, so timing does not reveal which one (or whether any) matched.
        Guid? credential = null;
        foreach (var (id, hash) in station.ActiveCredentials)
        {
            if (Secrets.Matches(request.Password ?? "", hash))
            {
                credential = id;
            }
        }
        if (credential is null)
        {
            return SourceAuthDecision.Deny("wrong or revoked credential");
        }
        var format = match.Groups["format"].Value == "mp3" ? AudioFormat.Mp3 : AudioFormat.Opus;
        if (!station.Plan.Allows(format))
        {
            return SourceAuthDecision.Deny($"format {match.Groups["format"].Value} not in the {station.Plan.Name} plan");
        }
        switch (DeclaredKbps(request))
        {
            case { Declared: true, Kbps: null }:
                // Present but unreadable (or beyond any real bitrate): never let it skip the plan limit.
                return SourceAuthDecision.Deny("unreadable declared bitrate");
            case { Kbps: { } kbps } when kbps > station.Plan.MaxBitrateKbps:
                return SourceAuthDecision.Deny($"bitrate {kbps} kbps above the {station.Plan.Name} plan's {station.Plan.MaxBitrateKbps} kbps");
        }
        return new SourceAuthDecision(true, "", credential, format);
    }

    /// <summary>
    /// Ice-Bitrate, else bitrate= in Ice-Audio-Info. Not declared: allowed (egress monitoring catches abuse).
    /// Declared but not a whole number in range: <c>Declared</c> with a null <c>Kbps</c>.
    /// </summary>
    public static (bool Declared, long? Kbps) DeclaredKbps(SourceAuthRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request.IceBitrate?.Trim();
        if (string.IsNullOrEmpty(value) && AudioInfoBitrate().Match(request.IceAudioInfo ?? "") is { Success: true } match)
        {
            value = match.Groups[1].Value;
        }
        if (string.IsNullOrEmpty(value))
        {
            return (false, null);
        }
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kbps) ? (true, kbps) : (true, null);
    }
}
