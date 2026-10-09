using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>Drives the API the way the SPA does: cookie session plus the antiforgery header.</summary>
public sealed partial class Browser(DashboardFactory factory)
{
    public const string Password = "correct horse battery staple";

    /// <summary>The API's JSON settings: web defaults plus enums by name.</summary>
    public static System.Text.Json.JsonSerializerOptions Json { get; } = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public HttpClient Http { get; } = factory.CreateBrowser();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Signs up and confirms with the emailed link; returns the user ID.</summary>
    public async Task<Guid> SignUpAsync(string email, string password = Password)
    {
        (await Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password }, Token)).EnsureSuccessStatusCode();
        var link = LinkIn(factory.Emails.Last(email).TextBody);
        var query = QueryHelpers.ParseQuery(link.Query);
        var userId = Guid.Parse(query["userId"]!);
        (await Http.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, code = query["code"].ToString() }, Token))
            .EnsureSuccessStatusCode();
        return userId;
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password = Password)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password });
        // The antiforgery token is bound to the user: fetch a new one for the signed-in session.
        await RefreshAntiforgeryAsync();
        return response;
    }

    public async Task<Guid> SignUpAndLoginAsync(string email)
    {
        var id = await SignUpAsync(email);
        (await LoginAsync(email)).EnsureSuccessStatusCode();
        return id;
    }

    public string? AntiforgeryToken { get; private set; }
    public Guid? TenantId { get; set; }

    public async Task RefreshAntiforgeryAsync()
    {
        var response = await Http.GetAsync("/api/v1/auth/antiforgery", Token);
        response.EnsureSuccessStatusCode();
        AntiforgeryToken = response.Headers.GetValues("Set-Cookie")
            .Select(c => XsrfCookie().Match(c)).First(m => m.Success).Groups[1].Value;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool antiforgery = true)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (antiforgery && AntiforgeryToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", AntiforgeryToken);
        }
        if (TenantId is { } tenant)
        {
            request.Headers.Add("X-Tenant-Id", tenant.ToString());
        }
        return await Http.SendAsync(request, Token);
    }

    public static Uri LinkIn(string emailBody) => new(LinkPattern().Match(emailBody).Value);

    /// <summary>Creates a tenant with this user as a member.</summary>
    public static async Task<Guid> CreateTenantAsync(DashboardFactory factory, Guid userId, MembershipRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = Tenant.Create($"Tenant {suffix}", $"tenant-{suffix}", DateTimeOffset.UtcNow);
        scope.ServiceProvider.GetRequiredService<CurrentTenant>().TenantId = tenant.Id;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(tenant);
        db.Memberships.Add(Membership.Create(tenant.Id, userId, role, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Token);
        return tenant.Id;
    }

    public static async Task AddMemberAsync(DashboardFactory factory, Guid tenantId, Guid userId, MembershipRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenant>().TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Memberships.Add(Membership.Create(tenantId, userId, role, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Token);
    }

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    public static string Unique(string name) => $"{name}-{Guid.NewGuid():N}@example.test";

    [GeneratedRegex(@"https://app\.test/\S+")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("^XSRF-TOKEN=([^;]+)")]
    private static partial Regex XsrfCookie();
}

internal static class ResponseAssertions
{
    public static void Status(this HttpResponseMessage response, HttpStatusCode expected)
        => Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {response.Content.ReadAsStringAsync().Result}");
}
