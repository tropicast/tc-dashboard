using Tropicast.Dashboard.Application.Security;
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
        Assert.True(Secrets.Matches(first, session.RefreshTokenHash));
        Assert.DoesNotContain(first, session.RefreshTokenHash, StringComparison.Ordinal);

        var second = session.Rotate(Now.AddDays(1));
        Assert.NotEqual(first, second);
        Assert.True(Secrets.Matches(second, session.RefreshTokenHash));
        Assert.True(Secrets.Matches(first, session.PreviousRefreshTokenHash!));
        Assert.Equal(Now.AddDays(1) + DeviceSession.Lifetime, session.ExpiresAt);
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
