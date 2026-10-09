using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Domain.Tenants;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class AuthTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_up_confirm_and_sign_in_with_a_hardened_session_cookie()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("owner");
        await browser.SignUpAsync(email);
        var response = await browser.LoginAsync(email);
        response.Status(HttpStatusCode.NoContent);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthSetup.SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await browser.Http.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Browser.Json, Token);
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task An_unconfirmed_account_cannot_sign_in_or_get_desktop_tokens()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("unconfirmed");
        (await browser.Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Browser.Password }, Token))
            .Status(HttpStatusCode.Accepted);
        (await browser.LoginAsync(email)).Status(HttpStatusCode.Unauthorized);
        (await browser.Http.PostAsJsonAsync("/api/v1/auth/token",
            new { email, password = Browser.Password, deviceName = "Studio PC" }, Token)).Status(HttpStatusCode.Unauthorized);
        (await browser.Http.GetAsync("/api/v1/auth/me", Token)).Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Answers_do_not_reveal_whether_an_email_has_an_account()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("existing");
        await browser.SignUpAsync(email);

        (await browser.Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Browser.Password }, Token))
            .Status(HttpStatusCode.Accepted);
        Assert.Contains("already has an account", factory.Emails.Last(email).TextBody, StringComparison.Ordinal);

        var unknown = await browser.LoginAsync(Browser.Unique("nobody"));
        var wrong = await browser.LoginAsync(email, "a wrong password for sure");
        unknown.Status(HttpStatusCode.Unauthorized);
        wrong.Status(HttpStatusCode.Unauthorized);
        Assert.Equal(await Title(unknown), await Title(wrong));
        (await browser.Http.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = Browser.Unique("nobody") }, Token))
            .Status(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var browser = new Browser(factory);
        var response = await browser.Http.PostAsJsonAsync("/api/v1/auth/register",
            new { email = Browser.Unique("weak"), password = "short" }, Token);
        response.Status(HttpStatusCode.BadRequest);
        Assert.Contains("password", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsafe_cookie_requests_need_the_antiforgery_token()
    {
        var browser = new Browser(factory);
        await browser.SignUpAndLoginAsync(Browser.Unique("csrf"));
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", antiforgery: false)).Status(HttpStatusCode.BadRequest);
        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/logout")).Status(HttpStatusCode.NoContent);
        (await browser.Http.GetAsync("/api/v1/auth/me", Token)).Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("locked");
        await browser.SignUpAsync(email);
        for (var i = 0; i < 5; i++)
        {
            (await browser.LoginAsync(email, "a wrong password for sure")).Status(HttpStatusCode.Unauthorized);
        }
        var locked = await browser.LoginAsync(email);
        locked.Status(HttpStatusCode.Unauthorized);
        Assert.Contains("Too many failed attempts", await Title(locked), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Desktop_tokens_rotate_and_revoking_signs_the_device_out_on_its_next_refresh()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("desktop");
        await browser.SignUpAsync(email);
        var desktop = factory.CreateClient();

        var first = await TokenAsync(desktop, email, "Studio PC");
        Assert.Equal("Bearer", first.TokenType);
        Assert.Equal(900, first.ExpiresIn);
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me"))
        {
            request.Headers.Authorization = Browser.Bearer(first.AccessToken);
            (await desktop.SendAsync(request, Token)).Status(HttpStatusCode.OK);
        }

        var second = await RefreshAsync(desktop, first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        (await desktop.PostAsJsonAsync("/api/v1/auth/token/revoke", new { refreshToken = second.RefreshToken }, Token))
            .Status(HttpStatusCode.NoContent);
        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = second.RefreshToken }, Token))
            .Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_ends_that_device_session()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("stolen");
        await browser.SignUpAsync(email);
        var desktop = factory.CreateClient();
        var first = await TokenAsync(desktop, email, "Laptop");
        var second = await RefreshAsync(desktop, first.RefreshToken);

        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = first.RefreshToken }, Token))
            .Status(HttpStatusCode.Unauthorized);
        // The legitimate holder of the newer token is signed out too: the session is compromised.
        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = second.RefreshToken }, Token))
            .Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_device_is_revoked_from_the_dashboard_and_the_others_keep_working()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("devices");
        await browser.SignUpAndLoginAsync(email);
        var desktop = factory.CreateClient();
        var studio = await TokenAsync(desktop, email, "Studio PC");
        var laptop = await TokenAsync(desktop, email, "Laptop");

        var devices = await browser.Http.GetFromJsonAsync<List<DeviceResponse>>("/api/v1/auth/devices", Browser.Json, Token);
        var studioDevice = devices!.Single(d => d.DeviceName == "Studio PC");
        (await browser.SendAsync(HttpMethod.Delete, $"/api/v1/auth/devices/{studioDevice.Id}")).Status(HttpStatusCode.NoContent);

        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = studio.RefreshToken }, Token))
            .Status(HttpStatusCode.Unauthorized);
        await RefreshAsync(desktop, laptop.RefreshToken);
    }

    [Fact]
    public async Task Password_reset_sets_a_new_password_and_signs_out_desktop_devices()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("reset");
        await browser.SignUpAsync(email);
        var desktop = factory.CreateClient();
        var device = await TokenAsync(desktop, email, "Studio PC");

        (await browser.Http.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email }, Token)).Status(HttpStatusCode.Accepted);
        var query = QueryHelpers.ParseQuery(Browser.LinkIn(factory.Emails.Last(email).TextBody).Query);
        const string newPassword = "a brand new long passphrase";
        (await browser.Http.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email, code = query["code"].ToString(), newPassword }, Token)).Status(HttpStatusCode.NoContent);

        (await browser.LoginAsync(email)).Status(HttpStatusCode.Unauthorized);
        (await browser.LoginAsync(email, newPassword)).Status(HttpStatusCode.NoContent);
        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = device.RefreshToken }, Token))
            .Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_browser_signed_in_and_signs_out_devices()
    {
        var browser = new Browser(factory);
        var email = Browser.Unique("change");
        await browser.SignUpAndLoginAsync(email);
        var desktop = factory.CreateClient();
        var device = await TokenAsync(desktop, email, "Studio PC");

        (await browser.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = Browser.Password, newPassword = "another long passphrase" })).Status(HttpStatusCode.NoContent);
        (await browser.Http.GetAsync("/api/v1/auth/me", Token)).Status(HttpStatusCode.OK);
        (await desktop.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken = device.RefreshToken }, Token))
            .Status(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_broadcaster_cannot_manage_billing_or_stations_and_an_owner_can()
    {
        var owner = new Browser(factory);
        var ownerId = await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await Browser.CreateTenantAsync(factory, ownerId, MembershipRole.Owner);
        var broadcaster = new Browser(factory);
        var broadcasterId = await broadcaster.SignUpAndLoginAsync(Browser.Unique("dj"));
        await Browser.AddMemberAsync(factory, tenantId, broadcasterId, MembershipRole.Broadcaster);

        // Billing is Owner-only; deleting a station needs Admin. Every station endpoint uses these policies.
        Assert.True(await AllowedAsync(ownerId, tenantId, TenantPolicies.Owner));
        Assert.True(await AllowedAsync(ownerId, tenantId, TenantPolicies.Admin));
        Assert.False(await AllowedAsync(broadcasterId, tenantId, TenantPolicies.Owner));
        Assert.False(await AllowedAsync(broadcasterId, tenantId, TenantPolicies.Admin));
        Assert.True(await AllowedAsync(broadcasterId, tenantId, TenantPolicies.Broadcaster));
        Assert.True(await AllowedAsync(broadcasterId, tenantId, TenantPolicies.Member));

        // Enforced on an endpoint: inviting members needs Admin.
        owner.TenantId = tenantId;
        broadcaster.TenantId = tenantId;
        (await broadcaster.SendAsync(HttpMethod.Post, "/api/v1/tenants/current/invitations",
            new { email = Browser.Unique("friend"), role = "Broadcaster" })).Status(HttpStatusCode.Forbidden);
        (await owner.SendAsync(HttpMethod.Post, "/api/v1/tenants/current/invitations",
            new { email = Browser.Unique("friend"), role = "Broadcaster" })).Status(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_tenant_the_user_is_not_a_member_of_grants_nothing()
    {
        var owner = new Browser(factory);
        var ownerId = await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await Browser.CreateTenantAsync(factory, ownerId, MembershipRole.Owner);
        var outsider = new Browser(factory);
        var outsiderId = await outsider.SignUpAndLoginAsync(Browser.Unique("outsider"));
        await Browser.CreateTenantAsync(factory, outsiderId, MembershipRole.Owner);

        outsider.TenantId = tenantId;
        (await outsider.SendAsync(HttpMethod.Post, "/api/v1/tenants/current/invitations",
            new { email = Browser.Unique("x"), role = "Broadcaster" })).Status(HttpStatusCode.Forbidden);
        Assert.False(await AllowedAsync(outsiderId, tenantId, TenantPolicies.Member));
    }

    [Fact]
    public async Task Invitations_are_accepted_once_by_the_invited_address_only()
    {
        var owner = new Browser(factory);
        var ownerId = await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        owner.TenantId = await Browser.CreateTenantAsync(factory, ownerId, MembershipRole.Owner);
        var inviteeEmail = Browser.Unique("invitee");
        (await owner.SendAsync(HttpMethod.Post, "/api/v1/tenants/current/invitations",
            new { email = inviteeEmail, role = "Admin" })).Status(HttpStatusCode.Created);
        var token = QueryHelpers.ParseQuery(Browser.LinkIn(factory.Emails.Last(inviteeEmail).TextBody).Query)["token"].ToString();

        var stranger = new Browser(factory);
        await stranger.SignUpAndLoginAsync(Browser.Unique("stranger"));
        (await stranger.SendAsync(HttpMethod.Post, "/api/v1/invitations/accept", new { token })).Status(HttpStatusCode.Forbidden);

        var invitee = new Browser(factory);
        await invitee.SignUpAndLoginAsync(inviteeEmail);
        (await invitee.SendAsync(HttpMethod.Post, "/api/v1/invitations/accept", new { token })).Status(HttpStatusCode.NoContent);
        var me = await invitee.Http.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Browser.Json, Token);
        Assert.Equal(MembershipRole.Admin, Assert.Single(me!.Memberships).Role);
        (await invitee.SendAsync(HttpMethod.Post, "/api/v1/invitations/accept", new { token })).Status(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_an_owner_can_invite_an_owner()
    {
        var owner = new Browser(factory);
        var ownerId = await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        var tenantId = await Browser.CreateTenantAsync(factory, ownerId, MembershipRole.Owner);
        var admin = new Browser(factory) { TenantId = tenantId };
        await Browser.AddMemberAsync(factory, tenantId, await admin.SignUpAndLoginAsync(Browser.Unique("admin")), MembershipRole.Admin);
        (await admin.SendAsync(HttpMethod.Post, "/api/v1/tenants/current/invitations",
            new { email = Browser.Unique("boss"), role = "Owner" })).Status(HttpStatusCode.Forbidden);
    }

    private async Task<bool> AllowedAsync(Guid userId, Guid tenantId, string policy)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/v1/check";
        context.Request.Headers[TenantAccess.Header] = tenantId.ToString();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        await new TenantAccessMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(context.User, policy);
        return result.Succeeded;
    }

    private static async Task<TokenResponse> TokenAsync(HttpClient client, string email, string device)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new { email, password = Browser.Password, deviceName = device }, Token);
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
    }

    private static async Task<TokenResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/token/refresh", new { refreshToken }, Token);
        response.Status(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Token))!;
    }

    private static async Task<string> Title(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Token))!.Title!;
}
