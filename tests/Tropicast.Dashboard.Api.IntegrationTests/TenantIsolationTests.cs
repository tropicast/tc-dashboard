using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.IntegrationTests;

/// <summary>
/// Every tenant-scoped endpoint, found from the routing table (so a new endpoint is covered without editing this
/// test), called by a member of another tenant with tenant A's IDs: never a success, never A's data.
/// </summary>
public sealed partial class TenantIsolationTests(DashboardFactory factory) : IClassFixture<DashboardFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly HashSet<string> TenantPolicyNames =
        [TenantPolicies.Member, TenantPolicies.Broadcaster, TenantPolicies.Admin, TenantPolicies.Owner];

    private sealed record Victim(Guid TenantId, Guid OwnerId, StationResponse Station, Guid CredentialId, Guid TrackId, Guid PlaylistId);

    private async Task<Victim> VictimAsync()
    {
        var owner = new Browser(factory);
        var ownerId = await owner.SignUpAndLoginAsync(Browser.Unique("victim"));
        var tenantId = await owner.CreateTenantAsync();
        var station = (await (await owner.SendAsync(HttpMethod.Post, "/api/v1/stations", new { name = "Victim Radio Secret Name", slug = "victim" }))
            .Content.ReadFromJsonAsync<StationResponse>(Browser.Json, Token))!;
        var credential = await (await owner.SendAsync(HttpMethod.Post, $"/api/v1/stations/{station.Id}/credentials", new { deviceLabel = "Studio" }))
            .Content.ReadFromJsonAsync<IssuedCredentialResponse>(Browser.Json, Token);
        // A stored track of the victim's station (uploads are covered in TrackTests).
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var track = Domain.Media.Track.StartUpload(station.Id, "victim.mp3", 1000, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        track.Confirm(Domain.Media.TrackFormat.Mp3, TimeSpan.FromSeconds(30), 1000, "Victim track", "");
        db.Tracks.Add(track);
        var playlist = Domain.Media.Playlist.Create(station.Id, "Victim playlist", false, DateTimeOffset.UtcNow);
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(Token);
        return new Victim(tenantId, ownerId, station, credential!.Credential.Id, track.Id, playlist.Id);
    }

    private List<(string Method, string Template)> TenantEndpoints()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy is { } policy && TenantPolicyNames.Contains(policy)))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (m, "/" + e.RoutePattern.RawText!.TrimStart('/'))))
            .Distinct()
            .ToList();
        Assert.True(endpoints.Count >= 15, $"Only {endpoints.Count} tenant endpoints found: the policy lookup is broken.");
        return endpoints;
    }

    private static string Fill(string template, Victim victim) => RouteParameter().Replace(template, m => m.Groups["name"].Value switch
    {
        "id" or "stationId" => victim.Station.Id.ToString(),
        "credentialId" => victim.CredentialId.ToString(),
        "trackId" => victim.TrackId.ToString(),
        "playlistId" => victim.PlaylistId.ToString(),
        "userId" => victim.OwnerId.ToString(),
        var other => throw new InvalidOperationException($"Unknown route parameter {{{other}}} in {template}: teach the isolation test."),
    });

    [Fact]
    public async Task Another_tenant_never_reads_or_changes_tenant_data()
    {
        var victim = await VictimAsync();
        var attacker = new Browser(factory);
        await attacker.SignUpAndLoginAsync(Browser.Unique("attacker"));
        var attackerTenant = await attacker.CreateTenantAsync();
        var failures = new List<string>();

        foreach (var (method, template) in TenantEndpoints())
        {
            var path = Fill(template, victim);
            // Own tenant selected, victim's IDs in the path: not found (or forbidden).
            // Endpoints without IDs (the current tenant, lists) may succeed: on the attacker's own data only.
            attacker.TenantId = attackerTenant;
            await CheckAsync(attacker, method, path, "own tenant", failures, victim, successAllowed: !template.Contains('{'));
            // Victim's tenant selected: not a member, so forbidden.
            attacker.TenantId = victim.TenantId;
            await CheckAsync(attacker, method, path, "victim tenant", failures, victim, expectForbidden: true);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var station = await db.Stations.IgnoreQueryFilters().SingleAsync(s => s.Id == victim.Station.Id, Token);
        Assert.Equal(("Victim Radio Secret Name", (DateTimeOffset?)null), (station.Name, station.DeletedAt));
        Assert.Null((await db.BroadcastCredentials.IgnoreQueryFilters().SingleAsync(c => c.Id == victim.CredentialId, Token)).RevokedAt);
        Assert.True(await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == victim.OwnerId && m.TenantId == victim.TenantId, Token));
        Assert.True(await db.Tracks.IgnoreQueryFilters().AnyAsync(t => t.Id == victim.TrackId, Token));
        Assert.Equal("Victim playlist", (await db.Playlists.IgnoreQueryFilters().SingleAsync(p => p.Id == victim.PlaylistId, Token)).Name);
        Assert.False(await db.AutoDjSettings.IgnoreQueryFilters().AnyAsync(a => a.StationId == victim.Station.Id, Token));
    }

    private static async Task CheckAsync(Browser attacker, string method, string path, string label, List<string> failures, Victim victim,
        bool expectForbidden = false, bool successAllowed = false)
    {
        using var response = await attacker.SendAsync(new HttpMethod(method), path, method is "GET" or "DELETE" ? null : new { });
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(Token);
        if (status >= 500 || (status < 400 && !successAllowed))
        {
            failures.Add($"{method} {path} ({label}): {status}");
        }
        else if (expectForbidden && response.StatusCode != HttpStatusCode.Forbidden)
        {
            failures.Add($"{method} {path} ({label}): expected 403, got {status}");
        }
        if (body.Contains(victim.Station.Name, StringComparison.Ordinal) || body.Contains(victim.Station.PublicId, StringComparison.Ordinal))
        {
            failures.Add($"{method} {path} ({label}): response contains the victim's station");
        }
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();
}
