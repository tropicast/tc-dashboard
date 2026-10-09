using Tropicast.Dashboard.Domain.Tenants;

namespace Tropicast.Dashboard.Domain.Tests;

public sealed class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static Invitation Create() => Invitation.Create(Guid.NewGuid(), "DJ@Example.test", MembershipRole.Broadcaster,
        new string('c', 64), Guid.NewGuid(), Now);

    [Fact]
    public void Accepting_grants_the_role_once()
    {
        var invitation = Create();
        var userId = Guid.NewGuid();
        var membership = invitation.Accept(userId, Now.AddDays(1));
        Assert.Equal((invitation.TenantId, userId, MembershipRole.Broadcaster), (membership.TenantId, membership.UserId, membership.Role));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddDays(1)));
    }

    [Fact]
    public void Invitations_expire_after_seven_days()
    {
        var invitation = Create();
        Assert.True(invitation.IsOpen(Now + Invitation.Lifetime - TimeSpan.FromSeconds(1)));
        Assert.False(invitation.IsOpen(Now + Invitation.Lifetime));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now + Invitation.Lifetime));
    }

    [Fact]
    public void The_email_is_matched_case_insensitively_and_stored_lowercase()
    {
        var invitation = Create();
        Assert.Equal("dj@example.test", invitation.Email);
        Assert.True(invitation.IsFor("dj@EXAMPLE.test"));
        Assert.False(invitation.IsFor("other@example.test"));
    }
}
