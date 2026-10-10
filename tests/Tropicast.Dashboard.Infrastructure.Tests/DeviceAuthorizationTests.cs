using Tropicast.Dashboard.Infrastructure.Identity;

namespace Tropicast.Dashboard.Infrastructure.Tests;

public sealed class DeviceAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_device_code_is_secret_and_the_user_code_easy_to_type()
    {
        var (request, deviceCode) = DeviceAuthorization.Start(" Studio PC ", Now);
        Assert.Equal("Studio PC", request.DeviceName);
        Assert.True(request.Matches(deviceCode));
        Assert.False(request.Matches($"{request.Id:N}.wrong"));
        Assert.DoesNotContain(deviceCode.Split('.')[1], request.DeviceCodeHash, StringComparison.Ordinal);
        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{8}$", request.UserCode);
        Assert.Equal(request.UserCode, DeviceAuthorization.NormalizeUserCode(request.DisplayUserCode.ToLowerInvariant()));
        Assert.Equal(Now + DeviceAuthorization.Lifetime, request.ExpiresAt);
    }

    [Theory]
    [InlineData("bcdf-ghjk", "BCDFGHJK")]
    [InlineData(" BCDF GHJK ", "BCDFGHJK")]
    [InlineData("BCDF-GHJ", null)]
    [InlineData("BCDF-GHJA", null)]
    [InlineData("BCDF-GHJ1", null)]
    [InlineData(null, null)]
    public void User_codes_are_normalized(string? typed, string? stored)
        => Assert.Equal(stored, DeviceAuthorization.NormalizeUserCode(typed));

    [Fact]
    public void Polls_wait_for_approval_and_succeed_once()
    {
        var (request, _) = DeviceAuthorization.Start("Studio PC", Now);
        Assert.Equal(DeviceAuthorizationState.Pending, request.Poll(Now));
        Assert.Equal(DeviceAuthorizationState.SlowDown, request.Poll(Now.AddSeconds(2)));
        request.Approve(Guid.NewGuid(), Now.AddSeconds(3));
        Assert.False(request.IsPending(Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => request.Deny(Now.AddSeconds(3)));
        Assert.Equal(DeviceAuthorizationState.Approved, request.Poll(Now.AddSeconds(5)));
        Assert.Equal(DeviceAuthorizationState.Expired, request.Poll(Now.AddSeconds(15)));
    }

    [Fact]
    public void Denied_and_expired_requests_end()
    {
        var (denied, _) = DeviceAuthorization.Start("Studio PC", Now);
        denied.Deny(Now);
        Assert.Equal(DeviceAuthorizationState.Denied, denied.Poll(Now.AddSeconds(5)));

        var (expired, _) = DeviceAuthorization.Start("Studio PC", Now);
        var later = Now + DeviceAuthorization.Lifetime;
        Assert.False(expired.IsPending(later));
        Assert.Throws<InvalidOperationException>(() => expired.Approve(Guid.NewGuid(), later));
        Assert.Equal(DeviceAuthorizationState.Expired, expired.Poll(later));
    }
}
