using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Email;
using Tropicast.Dashboard.Application.Outbox;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Downloads;

/// <summary>
/// "Your workspace is ready" (#127): sent to a new workspace's creator from the <c>TenantCreated</c> event, so a mail
/// failure never fails the creation (the outbox retries it). Carries the desktop app's download links.
/// </summary>
internal sealed class WelcomeEmail(IServiceScopeFactory scopes) : IOutboxConsumer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["win-x64"] = "Windows 10 or 11",
        ["osx-arm64"] = "macOS, Apple silicon (M1 or later)",
        ["osx-x64"] = "macOS, Intel",
        ["linux-x64"] = "Linux (AppImage)",
    };

    public IReadOnlyCollection<string> EventTypes { get; } = [nameof(TenantCreated)];

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var created = JsonSerializer.Deserialize<TenantCreated>(message.Payload, Json)
            ?? throw new JsonException("Empty TenantCreated payload.");
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .SingleOrDefaultAsync(t => t.Id == created.TenantId, cancellationToken);
        // Its creator is its first owner.
        var owner = await db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(m => m.TenantId == created.TenantId && m.Role == MembershipRole.Owner)
            .OrderBy(m => m.CreatedAt)
            .Join(db.Users, m => m.UserId, u => u.Id, (_, u) => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenant is null || string.IsNullOrEmpty(owner))
        {
            return; // Deleted since, or created without an owner (seeding): nobody to welcome.
        }
        var baseUrl = services.GetRequiredService<IOptions<AppOptions>>().Value.PublicBaseUrl;
        var links = await DownloadEndpoints.LinksAsync(services.GetRequiredService<StationReleases>(), baseUrl, cancellationToken);
        await services.GetRequiredService<IEmailSender>().SendAsync(Message(owner, baseUrl, tenant.Name, links), cancellationToken);
    }

    internal static EmailMessage Message(string to, string baseUrl, string tenantName, StationDownloadsResponse downloads)
    {
        var root = baseUrl.TrimEnd('/');
        var installers = downloads.Files.Where(f => Labels.ContainsKey(f.Platform)).ToList();
        List<string> app = installers.Count == 0
            ? [$"   Download it for your computer: {downloads.ReleasesPage}"]
            : [$"   Version {downloads.Version}, for your computer:", .. installers.Select(f => $"   - {Labels[f.Platform]}: {f.Url}")];
        if (installers.Count > 0 && downloads.Files.FirstOrDefault(f => f.Platform == "readme") is { } readme)
        {
            app.Add($"   How to install, and what to do if it does not work: {readme.Url}");
        }
        return new EmailMessage(to, $"Your Tropicast workspace {tenantName} is ready",
            $"""
            Your workspace {tenantName} is ready on Tropicast.

            1. Create your first station, if you have not yet:
               {root}/stations

            2. Install Tropicast Station, the desktop app you broadcast with.
            {string.Join('\n', app)}

            3. Open the app, click "Sign in with Tropicast" and approve the code it shows at:
               {root}/device

            Then choose your audio source and click Go Live.
            """);
    }
}
