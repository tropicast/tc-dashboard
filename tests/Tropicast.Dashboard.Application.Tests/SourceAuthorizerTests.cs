using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Application.SourceAuth;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Tests;

/// <summary>The contract's rules, case by case (tc-streaming docs/source-auth.md and auth-stub/server.py).</summary>
public sealed class SourceAuthorizerTests
{
    private const string Station = "k3m9x2p7qa";
    private const string Secret = "the-device-secret";
    private static readonly Guid Credential = Guid.NewGuid();

    private static SourceAuthStation Free(params string[] secrets) => new(Station, true, Plan.Free,
        secrets.Length == 0 ? [(Credential, Secrets.Hash(Secret))] : [.. secrets.Select(s => (Guid.NewGuid(), Secrets.Hash(s)))]);

    private static SourceAuthRequest Request(string mount = $"/stations/{Station}/live.mp3", string user = Station,
        string password = Secret, string? bitrate = null, string? audioInfo = null) => new(mount, user, password, bitrate, audioInfo);

    [Fact]
    public void The_station_publishes_to_its_own_mount_with_an_active_credential()
    {
        var decision = SourceAuthorizer.Decide(Request(), Free());
        Assert.True(decision.Allowed);
        Assert.Equal((Credential, AudioFormat.Mp3), (decision.CredentialId!.Value, decision.Format!.Value));
        Assert.Equal(AudioFormat.Opus, SourceAuthorizer.Decide(Request($"/stations/{Station}/live.opus"), Free()).Format);
    }

    [Theory]
    [InlineData("/other.mp3", Station)]
    [InlineData($"/stations/{Station}/live.aac", Station)]
    [InlineData($"/stations/{Station}/live.mp3/extra", Station)]
    [InlineData($"/stations/{Station}/live.mp3", "source")]
    [InlineData("/stations/other1234/live.mp3", Station)]
    public void Mounts_outside_the_layout_and_other_users_are_denied(string mount, string user)
        => Assert.False(SourceAuthorizer.Decide(Request(mount, user), Free()).Allowed);

    [Fact]
    public void Wrong_revoked_or_missing_credentials_are_denied_without_echoing_them()
    {
        var wrong = SourceAuthorizer.Decide(Request(password: "guess"), Free());
        Assert.False(wrong.Allowed);
        Assert.DoesNotContain("guess", wrong.Reason, StringComparison.Ordinal);
        // Revoked credentials are not in the active list.
        Assert.False(SourceAuthorizer.Decide(Request(), new SourceAuthStation(Station, true, Plan.Free, [])).Allowed);
        Assert.False(SourceAuthorizer.Decide(Request(), null).Allowed);
        Assert.True(SourceAuthorizer.Decide(Request(), Free("other-device", Secret)).Allowed);
    }

    [Fact]
    public void A_suspended_tenant_cannot_broadcast()
        => Assert.False(SourceAuthorizer.Decide(Request(), Free() with { TenantActive = false }).Allowed);

    [Theory]
    [InlineData("64", null, true)]
    [InlineData("65", null, false)]
    [InlineData(null, "bitrate=128;samplerate=44100;channels=2", false)]
    [InlineData(null, "samplerate=44100;ice-bitrate=48", true)]
    [InlineData("", "", true)]
    [InlineData(null, null, true)]
    [InlineData("99999999999999999999", null, false)]
    [InlineData("64k", null, false)]
    [InlineData(null, "bitrate=99999999999999999999999", false)]
    public void A_declared_bitrate_must_fit_the_plan(string? iceBitrate, string? audioInfo, bool allowed)
        => Assert.Equal(allowed, SourceAuthorizer.Decide(Request(bitrate: iceBitrate, audioInfo: audioInfo), Free()).Allowed);
}
