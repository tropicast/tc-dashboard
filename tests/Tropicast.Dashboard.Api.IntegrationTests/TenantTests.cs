using System.Net;
using System.Net.Http.Json;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Tenants;
using Tropicast.Dashboard.Domain.Tenants;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class TenantTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_signed_in_user_creates_a_tenant_and_owns_it()
    {
        var user = new Browser(factory);
        await user.SignUpAndLoginAsync(Browser.Unique("founder"));
        var response = await user.SendAsync(HttpMethod.Post, "/api/v1/tenants", new { name = "Radio Group", slug = $"g-{Guid.NewGuid():N}"[..20] });
        response.Status(HttpStatusCode.Created);
        var tenant = (await response.Content.ReadFromJsonAsync<TenantResponse>(Browser.Json, Token))!;
        Assert.Equal(("free", 0, TenantStatus.Active), (tenant.Plan.Id, tenant.StationCount, tenant.Status));
        Assert.Equal(["mp3", "opus"], tenant.Plan.Formats);

        var me = await user.Http.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Browser.Json, Token);
        Assert.Equal(MembershipRole.Owner, Assert.Single(me!.Memberships, m => m.TenantId == tenant.Id).Role);

        (await user.SendAsync(HttpMethod.Post, "/api/v1/tenants", new { name = "Copy", slug = tenant.Slug })).Status(HttpStatusCode.Conflict);
        (await factory.CreateBrowser().PostAsJsonAsync("/api/v1/tenants", new { name = "X", slug = "anonymous-x" }, Token))
            .Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_current_tenant_is_read_by_members_and_renamed_by_admins_with_if_match()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        var broadcaster = new Browser(factory) { TenantId = tenantId };
        await Browser.AddMemberAsync(factory, tenantId, await broadcaster.SignUpAndLoginAsync(Browser.Unique("dj")), MembershipRole.Broadcaster);

        var read = await broadcaster.SendAsync(HttpMethod.Get, "/api/v1/tenants/current");
        read.Status(HttpStatusCode.OK);
        var etag = read.Headers.ETag!.ToString();
        (await broadcaster.SendIfMatchAsync(HttpMethod.Patch, "/api/v1/tenants/current", etag, new { name = "Hijack" }))
            .Status(HttpStatusCode.Forbidden);

        (await owner.SendIfMatchAsync(HttpMethod.Patch, "/api/v1/tenants/current", null, new { name = "Renamed" }))
            .Status(HttpStatusCode.PreconditionRequired);
        var renamed = await owner.SendIfMatchAsync(HttpMethod.Patch, "/api/v1/tenants/current", etag, new { name = "Renamed" });
        renamed.Status(HttpStatusCode.OK);
        Assert.Equal("Renamed", (await renamed.Content.ReadFromJsonAsync<TenantResponse>(Browser.Json, Token))!.Name);
        (await owner.SendIfMatchAsync(HttpMethod.Patch, "/api/v1/tenants/current", etag, new { name = "Stale" }))
            .Status(HttpStatusCode.PreconditionFailed);
        (await owner.SendIfMatchAsync(HttpMethod.Patch, "/api/v1/tenants/current", renamed.Headers.ETag!.ToString(), new { slug = "Not Valid" }))
            .Status(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Members_are_listed_and_removed_under_the_owner_rules()
    {
        var owner = new Browser(factory);
        var ownerId = await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await owner.CreateTenantAsync();
        var admin = new Browser(factory) { TenantId = tenantId };
        var adminId = await admin.SignUpAndLoginAsync(Browser.Unique("admin"));
        await Browser.AddMemberAsync(factory, tenantId, adminId, MembershipRole.Admin);
        var dj = new Browser(factory) { TenantId = tenantId };
        var djId = await dj.SignUpAndLoginAsync(Browser.Unique("dj"));
        await Browser.AddMemberAsync(factory, tenantId, djId, MembershipRole.Broadcaster);

        var members = await (await dj.SendAsync(HttpMethod.Get, "/api/v1/tenants/current/members"))
            .Content.ReadFromJsonAsync<List<MemberResponse>>(Browser.Json, Token);
        Assert.Equal([MembershipRole.Owner, MembershipRole.Admin, MembershipRole.Broadcaster], members!.Select(m => m.Role));

        (await dj.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{adminId}")).Status(HttpStatusCode.Forbidden);
        (await admin.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{ownerId}")).Status(HttpStatusCode.Forbidden);
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{ownerId}")).Status(HttpStatusCode.Conflict);
        (await admin.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{djId}")).Status(HttpStatusCode.NoContent);
        (await admin.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{djId}")).Status(HttpStatusCode.NotFound);
        // The removed member no longer has access.
        (await dj.SendAsync(HttpMethod.Get, "/api/v1/tenants/current")).Status(HttpStatusCode.Forbidden);
    }
}

public sealed class LastOwnerTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    [Fact]
    public async Task Two_owners_removing_each_other_at_once_leave_one_owner()
    {
        var first = new Browser(factory);
        var firstId = await first.SignUpAndLoginAsync(Browser.Unique("first"));
        var tenantId = await first.CreateTenantAsync();
        var second = new Browser(factory) { TenantId = tenantId };
        var secondId = await second.SignUpAndLoginAsync(Browser.Unique("second"));
        await Browser.AddMemberAsync(factory, tenantId, secondId, MembershipRole.Owner);

        var results = await Task.WhenAll(
            first.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{secondId}"),
            second.SendAsync(HttpMethod.Delete, $"/api/v1/tenants/current/members/{firstId}"));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.NoContent);

        var survivor = results[0].StatusCode == HttpStatusCode.NoContent ? first : second;
        var members = await (await survivor.SendAsync(HttpMethod.Get, "/api/v1/tenants/current/members"))
            .Content.ReadFromJsonAsync<List<MemberResponse>>(Browser.Json, TestContext.Current.CancellationToken);
        Assert.Equal(MembershipRole.Owner, Assert.Single(members!).Role);
    }
}
