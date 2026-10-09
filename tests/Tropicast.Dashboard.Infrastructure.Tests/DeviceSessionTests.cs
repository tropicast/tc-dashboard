using Tropicast.Dashboard.Infrastructure.Identity;

namespace Tropicast.Dashboard.Infrastructure.Tests;

public sealed class DeviceSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_hashes_are_stored_and_rotation_replaces_the_token()
    {
        var (session, first) = DeviceSession.Start(Guid.NewGuid(), " Studio PC ", Now);
        Assert.Equal("Studio PC", session.DeviceName);
        Assert.True(session.IsCurrent(first));
        Assert.True(DeviceSession.TryGetSessionId(first, out var id) && id == session.Id);
        Assert.DoesNotContain(first.Split('.')[1], session.RefreshTokenHash, StringComparison.Ordinal);

        var second = session.Rotate(Now.AddDays(1));
        var third = session.Rotate(Now.AddDays(2));
        Assert.True(session.IsCurrent(third));
        Assert.False(session.IsCurrent(second));
        Assert.False(session.IsCurrent(first));
        Assert.Equal(Now.AddDays(2) + DeviceSession.Lifetime, session.ExpiresAt);
    }

    [Fact]
    public void Sessions_end_when_revoked_or_expired()
    {
        var (session, _) = DeviceSession.Start(Guid.NewGuid(), "Laptop", Now);
        Assert.True(session.IsActive(Now));
        Assert.False(session.IsActive(Now + DeviceSession.Lifetime));
        session.Revoke(Now);
        Assert.False(session.IsActive(Now));
    }
}

public sealed class RefreshTokenFormatTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData(".secret")]
    [InlineData("not-a-guid.secret")]
    public void Malformed_tokens_name_no_session(string? token) => Assert.False(DeviceSession.TryGetSessionId(token, out _));
}
