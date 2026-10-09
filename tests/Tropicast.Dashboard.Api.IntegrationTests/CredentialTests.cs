using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

public sealed class CredentialTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Browser Owner, StationResponse Station)> StationAsync()
    {
        var owner = new Browser(factory);
        await owner.SignUpAndLoginAsync(Browser.Unique("owner"));
        await owner.CreateTenantAsync();
        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Radio", slug = "radio" });
        response.Status(HttpStatusCode.Created);
        return (owner, (await response.Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!);
    }

    private static async Task<IssuedCredentialResponse> IssueAsync(Browser owner, Guid stationId, string label)
    {
        var response = await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{stationId}/credentials", new { deviceLabel = label });
        response.Status(HttpStatusCode.Created);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<IssuedCredentialResponse>(Browser.Json, Token))!;
    }

    [Fact]
    public async Task The_secret_is_shown_once_and_only_its_hash_is_stored()
    {
        var (owner, station) = await StationAsync();
        var issued = await IssueAsync(owner, station.Id, "Studio PC");
        Assert.Equal(station.PublicId, issued.Username);
        Assert.Equal(43, issued.Secret.Length);

        var list = await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/credentials");
        var body = await list.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(issued.Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Studio PC", Assert.Single((await list.Content.ReadFromJsonAsync<List<CredentialResponse>>(Browser.Json, Token))!).DeviceLabel);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.BroadcastCredentials.IgnoreQueryFilters().SingleAsync(c => c.Id == issued.Credential.Id, Token);
        Assert.Equal(Secrets.Hash(issued.Secret), stored.SecretHash);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(issued.Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_device_is_revoked_alone_and_its_label_can_be_reissued()
    {
        var (owner, station) = await StationAsync();
        var studio = await IssueAsync(owner, station.Id, "Studio PC");
        var laptop = await IssueAsync(owner, station.Id, "Laptop");
        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/credentials", new { deviceLabel = "Studio PC" }))
            .Status(HttpStatusCode.Conflict);

        var url = $"/api/v1/stations/{station.Id}/credentials/{studio.Credential.Id}";
        (await owner.SendAsync(HttpMethod.Delete, url)).Status(HttpStatusCode.NoContent);
        (await owner.SendAsync(HttpMethod.Delete, url)).Status(HttpStatusCode.NoContent);

        var credentials = (await (await owner.SendAsync(HttpMethod.Get, $"/api/v1/stations/{station.Id}/credentials"))
            .Content.ReadFromJsonAsync<List<CredentialResponse>>(Browser.Json, Token))!;
        Assert.Null(credentials.Single(c => c.Id == laptop.Credential.Id).RevokedAt);
        Assert.NotNull(credentials.Single(c => c.Id == studio.Credential.Id).RevokedAt);
        // Active first.
        Assert.Equal(laptop.Credential.Id, credentials[0].Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var revoked = await db.BroadcastCredentials.IgnoreQueryFilters().SingleAsync(c => c.Id == studio.Credential.Id, Token);
            Assert.False(revoked.IsActive);
        }
        await IssueAsync(owner, station.Id, "Studio PC");
    }

    [Fact]
    public async Task Create_and_revoke_are_audited_without_the_secret()
    {
        var (owner, station) = await StationAsync();
        var issued = await IssueAsync(owner, station.Id, "Studio PC");
        (await owner.SendAsync(HttpMethod.Delete, $"/api/v1/stations/{station.Id}/credentials/{issued.Credential.Id}"))
            .Status(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.AuditEntries.IgnoreQueryFilters()
            .Where(e => e.TargetId == issued.Credential.Id.ToString()).OrderBy(e => e.OccurredAt).ToListAsync(Token);
        Assert.Equal(["credential.created", "credential.revoked"], entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.Equal(owner.TenantId, e.TenantId));
        Assert.All(entries, e => Assert.NotNull(e.ActorUserId));
        Assert.All(entries, e => Assert.DoesNotContain(issued.Secret, e.Summary ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Broadcasters_cannot_manage_credentials_and_other_tenants_get_404()
    {
        var (owner, station) = await StationAsync();
        var issued = await IssueAsync(owner, station.Id, "Studio PC");
        var dj = new Browser(factory) { TenantId = owner.TenantId };
        await Browser.AddMemberAsync(factory, owner.TenantId!.Value, await dj.SignUpAndLoginAsync(Browser.Unique("dj")), MembershipRole.Broadcaster);
        var url = $"/api/v1/stations/{station.Id}/credentials";
        (await dj.SendAsync(HttpMethod.Get, url)).Status(HttpStatusCode.Forbidden);
        (await dj.SendAsync(HttpMethod.Post, url, new { deviceLabel = "Mine" })).Status(HttpStatusCode.Forbidden);
        (await dj.SendAsync(HttpMethod.Delete, $"{url}/{issued.Credential.Id}")).Status(HttpStatusCode.Forbidden);

        var (stranger, _) = await StationAsync();
        (await stranger.SendAsync(HttpMethod.Get, url)).Status(HttpStatusCode.NotFound);
        (await stranger.SendAsync(HttpMethod.Post, url, new { deviceLabel = "Steal" })).Status(HttpStatusCode.NotFound);
        (await stranger.SendAsync(HttpMethod.Delete, $"{url}/{issued.Credential.Id}")).Status(HttpStatusCode.NotFound);
        (await owner.SendAsync(HttpMethod.Post, url, new { deviceLabel = "" })).Status(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Concurrent_revokes_all_succeed_and_are_audited_once()
    {
        var (owner, station) = await StationAsync();
        var issued = await IssueAsync(owner, station.Id, "Studio PC");
        var url = $"/api/v1/stations/{station.Id}/credentials/{issued.Credential.Id}";
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => owner.SendAsync(HttpMethod.Delete, url)));
        Assert.All(results, r => r.Status(HttpStatusCode.NoContent));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.AuditEntries.IgnoreQueryFilters()
            .Where(e => e.TargetId == issued.Credential.Id.ToString() && e.Action == "credential.revoked").ToListAsync(Token));
    }
}
