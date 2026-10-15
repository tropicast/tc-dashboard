using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
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
/// <param name="Action">
/// <c>stream_auth</c> for a source connect, <see cref="SourceAuthorizer.MetadataAction"/> for an ICY metadata update.
/// </param>
public sealed record SourceAuthRequest(string? Mount, string? User, string? Password, string? IceBitrate, string? IceAudioInfo,
    string? Action = null);

/// <summary>The station data a decision needs. Null when the station does not exist or was deleted.</summary>
/// <param name="PublicId">The station's public ID.</param>
/// <param name="TenantActive">False when the tenant is suspended.</param>
/// <param name="Plan">The tenant's plan.</param>
/// <param name="ActiveCredentials">Active (unrevoked) credentials: ID and SHA-256 hash of the secret.</param>
/// <param name="StationRank">
/// Position of the station among its tenant's stations, oldest first (0-based). After a downgrade, only the first
/// <see cref="Plan.MaxStations"/> may broadcast.
/// </param>
/// <param name="AutoDjCredentials">The active AutoDJ credential(s): the only ones accepted on <c>autodj.*</c> mounts.</param>
/// <param name="AutoDjActive">AutoDJ is on and the plan includes it.</param>
public sealed record SourceAuthStation(string PublicId, bool TenantActive, Plan Plan,
    IReadOnlyList<(Guid Id, string SecretHash)> ActiveCredentials, int StationRank = 0,
    IReadOnlyList<(Guid Id, string SecretHash)>? AutoDjCredentials = null, bool AutoDjActive = false);

/// <summary>Allow with the matching credential, or deny with a reason safe to log (never the password).</summary>
/// <param name="Allowed">Let the source in.</param>
/// <param name="Reason">Why not; empty when allowed.</param>
/// <param name="CredentialId">The credential that matched.</param>
/// <param name="Format">The mount's format.</param>
/// <param name="AutoDj">The mount is the station's AutoDJ mount, not its live one.</param>
public sealed record SourceAuthDecision(bool Allowed, string Reason, Guid? CredentialId = null, AudioFormat? Format = null,
    bool AutoDj = false)
{
    public static SourceAuthDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// The source-auth rules: the mount is <c>/stations/{id}/live.(mp3|opus)</c> or, for AutoDJ,
/// <c>/stations/{id}/autodj.(mp3|opus)</c>; the user is that station; the password matches an active credential of the
/// mount's kind (constant time): device credentials on live mounts only, the AutoDJ credential on AutoDJ mounts only,
/// and only while AutoDJ is active; the plan allows the format; a declared bitrate is within the plan.
/// </summary>
public static partial class SourceAuthorizer
{
    /// <summary>Same mount layout the gateway and Icecast accept; <c>autodj</c> mounts are AutoDJ's fallback (#106).</summary>
    [GeneratedRegex(@"^/stations/(?<station>[A-Za-z0-9-]{1,64})/(?<kind>live|autodj)\.(?<format>mp3|opus)$")]
    private static partial Regex MountPattern();

    [GeneratedRegex(@"(?:^|;)\s*(?:ice-)?samplerate=(\d+)")]
    private static partial Regex AudioInfoSampleRate();

    [GeneratedRegex(@"(?:^|;)\s*(?:ice-)?channels=(\d+)")]
    private static partial Regex AudioInfoChannels();

    [GeneratedRegex(@"(?:^|;)\s*(?:ice-)?bitrate=(\d+)")]
    private static partial Regex AudioInfoBitrate();

    /// <summary>
    /// An ICY metadata update (tc-streaming#72): an MP3 source sends a track title as
    /// <c>GET /admin/metadata?mode=updinfo&amp;mount=…&amp;song=…</c> with its own credentials, and Icecast asks with this
    /// action. Checked as a connect to that mount, but it starts no session.
    /// </summary>
    public const string MetadataAction = "metadata_auth";

    /// <summary>
    /// The mount a metadata update names. Icecast sends the update's request URI as <c>mount</c>; null unless its path
    /// is <c>/admin/metadata</c>, <c>mode</c> is <c>updinfo</c> and the query has exactly one <c>mount</c>.
    /// </summary>
    public static string? MetadataMount(string? uri)
    {
        var separator = uri?.IndexOf('?', StringComparison.Ordinal) ?? -1;
        if (separator < 0 || !string.Equals(uri![..separator], "/admin/metadata", StringComparison.Ordinal))
        {
            return null;
        }
        var query = HttpUtility.ParseQueryString(uri[(separator + 1)..]);
        return query.GetValues("mode") is ["updinfo"] && query.GetValues("mount") is [var mount] ? mount : null;
    }

    /// <summary>The station a mount names, or null when the mount is outside the layout.</summary>
    public static string? StationOf(string? mount)
        => mount is not null && MountPattern().Match(mount) is { Success: true } match ? match.Groups["station"].Value : null;

    public static SourceAuthDecision Decide(SourceAuthRequest request, SourceAuthStation? station)
    {
        ArgumentNullException.ThrowIfNull(request);
        var match = MountPattern().Match(request.Mount ?? "");
        if (!match.Success)
        {
            return SourceAuthDecision.Deny("mount outside /stations/{id}/(live|autodj).(mp3|opus)");
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
        if (station.StationRank >= station.Plan.MaxStations)
        {
            return SourceAuthDecision.Deny($"the plan allows {station.Plan.MaxStations} station(s); upgrade or delete a station");
        }
        var autoDj = match.Groups["kind"].Value == "autodj";
        if (autoDj && !station.AutoDjActive)
        {
            return SourceAuthDecision.Deny("AutoDJ is off for this station");
        }
        // Check every active credential of the mount's kind, so timing does not reveal which one (or whether any) matched.
        Guid? credential = null;
        foreach (var (id, hash) in autoDj ? station.AutoDjCredentials ?? [] : station.ActiveCredentials)
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
        return new SourceAuthDecision(true, "", credential, format, autoDj);
    }

    /// <summary>
    /// The encoding a source declares in Ice-Audio-Info (bitrate, sample rate, channels), or null when it does not
    /// declare all three. AutoDJ pins it to match the live stream (#106).
    /// </summary>
    public static (int Kbps, int SampleRate, int Channels)? DeclaredEncoding(SourceAuthRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, kbps) = DeclaredKbps(request);
        var info = request.IceAudioInfo ?? "";
        return kbps is { } bitrate and <= int.MaxValue
            && AudioInfoSampleRate().Match(info) is { Success: true } rate
            && AudioInfoChannels().Match(info) is { Success: true } channels
            && int.TryParse(rate.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sampleRate)
            && int.TryParse(channels.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? ((int)bitrate, sampleRate, count)
            : null;
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
