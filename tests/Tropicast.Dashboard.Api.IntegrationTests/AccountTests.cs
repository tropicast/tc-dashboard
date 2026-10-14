using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Domain.Audit;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Terms acceptance, data export, account deletion and the cookie notice (#94).</summary>
public sealed class AccountTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_up_requires_accepting_the_terms_and_records_the_version()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("terms");
        var refused = await browser.Http.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = Browser.Password, acceptTerms = false }, Token);
        refused.Status(HttpStatusCode.BadRequest);
        Assert.Contains("acceptTerms", await refused.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Emails.Sent, m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));

        var before = DateTimeOffset.UtcNow;
        var userId = await browser.SignUpAndLoginAsync(email);
        var me = await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Token);
        Assert.Equal(Legal.TermsVersion, me.GetProperty("termsVersion").GetString());
        Assert.True(me.GetProperty("termsAccepted").GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == userId, Token);
        Assert.Equal(Legal.TermsVersion, user.TermsVersion);
        Assert.InRange(user.TermsAcceptedAt!.Value, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_user_on_an_older_version_is_asked_again_and_accepts_only_the_current_one()
    {
        var browser = new Browser(factory);
        var userId = await browser.SignUpAndLoginAsync(Browser.Unique("older"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.TermsVersion, "2000-01-01"), Token);
        }
        var me = await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Token);
        Assert.False(me.GetProperty("termsAccepted").GetBoolean());

        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/terms", new { version = "2000-01-01" })).Status(HttpStatusCode.BadRequest);
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/terms", new { version = Legal.TermsVersion })).Status(HttpStatusCode.NoContent);
        me = await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Token);
        Assert.True(me.GetProperty("termsAccepted").GetBoolean());
    }

    [Fact]
    public async Task Export_holds_the_account_memberships_devices_events_and_own_audit_entries_only()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("export");
        var userId = await browser.SignUpAndLoginAsync(email);
        var tenantId = await browser.CreateTenantAsync();
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" })).Status(HttpStatusCode.Created);
        // The desktop app signs in without the browser's cookie.
        (await new Browser(factory).Http.PostAsJsonAsync("/api/v1/auth/token",
            new { email, password = Browser.Password, deviceName = "Studio PC" }, Token)).Status(HttpStatusCode.OK);
        // Someone else's action in another tenant must not appear.
        var other = new Browser(factory);
        await other.SignUpAndLoginAsync(Browser.Unique("other"));
        await other.CreateTenantAsync();

        var response = await browser.Http.GetAsync("/api/v1/auth/account/export", Token);
        response.Status(HttpStatusCode.OK);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var export = await response.Content.ReadFromJsonAsync<JsonElement>(Token);

        var account = export.GetProperty("account");
        Assert.Equal(userId, account.GetProperty("id").GetGuid());
        Assert.Equal(email, account.GetProperty("email").GetString());
        Assert.Equal(Legal.TermsVersion, account.GetProperty("termsVersion").GetString());
        var membership = Assert.Single(export.GetProperty("memberships").EnumerateArray());
        Assert.Equal(tenantId, membership.GetProperty("tenantId").GetGuid());
        Assert.Equal("Owner", membership.GetProperty("role").GetString());
        Assert.Equal("Studio PC", Assert.Single(export.GetProperty("devices").EnumerateArray()).GetProperty("deviceName").GetString());
        Assert.Contains(export.GetProperty("securityEvents").EnumerateArray(), e => e.GetProperty("action").GetString() == "signin.succeeded");
        Assert.All(export.GetProperty("auditEntries").EnumerateArray(), e => Assert.Equal(tenantId, e.GetProperty("tenantId").GetGuid()));
        var json = export.GetRawText();
        Assert.DoesNotContain("refreshTokenHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delete_needs_the_password()
    {
        var browser = new Browser(factory);
        await browser.SignUpAndLoginAsync(Browser.Unique("wrongpw"));
        (await browser.SendAsync(HttpMethod.Delete, "/api/v1/auth/account", new { password = "not the password at all" }))
            .Status(HttpStatusCode.BadRequest);
        // A cross-site request carries the cookie but not the antiforgery token.
        var forged = await browser.SendAsync(HttpMethod.Delete, "/api/v1/auth/account", new { password = Browser.Password }, antiforgery: false);
        forged.Status(HttpStatusCode.BadRequest);
        Assert.Contains("antiforgery", await forged.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        (await browser.Http.GetAsync("/api/v1/auth/me", Token)).Status(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave_members_stations_or_a_paid_subscription_behind()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("lastowner"));
        var tenantId = await owner.CreateTenantAsync();

        var memberId = await new Browser(factory).SignUpAsync(Browser.Unique("member"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Memberships.Add(Membership.Create(tenantId, memberId, MembershipRole.Admin, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
        }
        await AssertRefusedAsync(owner, "remove the other members");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Memberships
                .IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(m => m.UserId == memberId).ExecuteDeleteAsync(Token);
        }

        (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" })).Status(HttpStatusCode.Created);
        await AssertRefusedAsync(owner, "Delete the stations");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Stations
                .IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(s => s.TenantId == tenantId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.DeletedAt, DateTimeOffset.UtcNow), Token);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Subscriptions.Add(Subscription.Create(tenantId, Plan.Starter.Id, SubscriptionStatus.Active, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
        }
        await AssertRefusedAsync(owner, "Cancel the subscription");
    }

    [Fact]
    public async Task Deleting_removes_the_account_signs_out_everywhere_and_keeps_shared_workspaces()
    {
        var owner = new Browser(factory);
        var ownerEmail = Browser.Unique("keeper");
        await owner.SignUpAndLoginAsync(ownerEmail);
        var sharedTenant = await owner.CreateTenantAsync();

        var leaver = new Browser(factory);
        var email = Browser.Unique("leaver");
        var userId = await leaver.SignUpAndLoginAsync(email);
        var ownTenant = await leaver.CreateTenantAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Memberships.Add(Membership.Create(sharedTenant, userId, MembershipRole.Admin, DateTimeOffset.UtcNow));
            db.AuditEntries.Add(AuditEntry.Record(sharedTenant, userId, "station.created", "Station", "x", null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
        }
        var tokens = await (await new Browser(factory).Http.PostAsJsonAsync("/api/v1/auth/token",
            new { email, password = Browser.Password, deviceName = "Laptop" }, Token)).Content.ReadFromJsonAsync<JsonElement>(Token);

        (await leaver.SendAsync(HttpMethod.Delete, "/api/v1/auth/account", new { password = Browser.Password })).Status(HttpStatusCode.NoContent);

        (await leaver.Http.GetAsync("/api/v1/auth/me", Token)).Status(HttpStatusCode.Unauthorized);
        (await leaver.LoginAsync(email)).Status(HttpStatusCode.Unauthorized);
        (await leaver.Http.PostAsJsonAsync("/api/v1/auth/token/refresh",
            new { refreshToken = tokens.GetProperty("refreshToken").GetString() }, Token)).Status(HttpStatusCode.Unauthorized);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id == userId, Token));
            Assert.False(await db.SecurityEvents.AnyAsync(e => e.UserId == userId, Token));
            Assert.False(await db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter]).AnyAsync(m => m.UserId == userId, Token));
            // Their own, otherwise empty workspace went with them; the shared one stays, without their name on it.
            var tenants = db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]);
            Assert.False(await tenants.AnyAsync(t => t.Id == ownTenant, Token));
            Assert.True(await tenants.AnyAsync(t => t.Id == sharedTenant, Token));
            var audit = db.AuditEntries.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(e => e.TenantId == sharedTenant);
            Assert.False(await audit.AnyAsync(e => e.ActorUserId == userId, Token));
            Assert.True(await audit.AnyAsync(e => e.Action == AccountEndpoints.AccountDeleted && e.ActorUserId == null, Token));
        }
        (await owner.Http.GetAsync("/api/v1/auth/me", Token)).Status(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_cookie_the_dashboard_sets_is_listed_in_the_cookie_notice()
    {
        var recorder = new SetCookieRecorder();
        var browser = new Browser(factory, factory.CreateDefaultClient(new Uri("https://localhost"), recorder, new CookieContainerHandler()));
        var email = Browser.Unique("cookies");
        await browser.SignUpAndLoginAsync(email);
        await browser.CreateTenantAsync();
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" })).Status(HttpStatusCode.Created);
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/terms", new { version = Legal.TermsVersion })).Status(HttpStatusCode.NoContent);
        (await browser.Http.GetAsync("/api/v1/auth/account/export", Token)).Status(HttpStatusCode.OK);
        (await browser.Http.GetAsync("/", Token)).EnsureSuccessStatusCode();
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/logout")).Status(HttpStatusCode.NoContent);

        var notice = LegalNotice();
        var documented = notice.GetProperty("cookies").EnumerateArray()
            .Where(c => c.GetProperty("kind").GetString() == "cookie").Select(c => c.GetProperty("name").GetString()).ToHashSet();
        Assert.NotEmpty(recorder.Names);
        Assert.Empty(recorder.Names.Except(documented));
    }

    [Fact]
    public void The_terms_version_matches_the_published_texts()
        => Assert.Equal(Legal.TermsVersion, LegalNotice().GetProperty("termsVersion").GetString());

    private static async Task AssertRefusedAsync(Browser browser, string reason)
    {
        var response = await browser.SendAsync(HttpMethod.Delete, "/api/v1/auth/account", new { password = Browser.Password });
        response.Status(HttpStatusCode.Conflict);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    /// <summary>web/src/legal/legal.json: the cookie list and terms version the SPA publishes.</summary>
    private static JsonElement LegalNotice()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tropicast.Dashboard.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "web", "src", "legal", "legal.json"))).RootElement;
    }

    /// <summary>Records the name of every cookie a response sets.</summary>
    private sealed class SetCookieRecorder : DelegatingHandler
    {
        public HashSet<string> Names { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                // A sign-out also expires cookies that were never set (Identity's external and two-factor ones): skip deletions.
                Names.UnionWith(cookies.Where(c => !c.Contains("=;", StringComparison.Ordinal))
                    .Select(c => c[..c.IndexOf('=', StringComparison.Ordinal)]));
            }
            return response;
        }
    }
}
