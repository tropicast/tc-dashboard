using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Domain.Audit;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

internal sealed record AcceptTermsRequest([property: Required, MaxLength(32)] string Version);
internal sealed record DeleteAccountRequest([property: Required] string Password);

/// <summary>Everything the dashboard stores about the signed-in user (GDPR art. 15 and 20).</summary>
internal sealed record AccountExport(DateTimeOffset ExportedAt, AccountExport.AccountData Account,
    IReadOnlyList<AccountExport.MembershipData> Memberships, IReadOnlyList<AccountExport.DeviceData> Devices,
    IReadOnlyList<AccountExport.SecurityEventData> SecurityEvents, IReadOnlyList<AccountExport.AuditData> AuditEntries)
{
    internal sealed record AccountData(Guid Id, string Email, bool EmailConfirmed, DateTimeOffset CreatedAt, string? TermsVersion,
        DateTimeOffset? TermsAcceptedAt);
    internal sealed record MembershipData(Guid TenantId, string TenantName, MembershipRole Role, DateTimeOffset Since);
    internal sealed record DeviceData(string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset ExpiresAt,
        DateTimeOffset? RevokedAt);
    internal sealed record SecurityEventData(string Action, string? IpAddress, string? Detail, DateTimeOffset OccurredAt);
    /// <summary>Actions the user took in a tenant's audit log.</summary>
    internal sealed record AuditData(Guid TenantId, string Action, string TargetType, string TargetId, string? Summary,
        DateTimeOffset OccurredAt);
}

/// <summary>
/// The user's own account (#94): accepting a new version of the legal texts, exporting their data and deleting the
/// account.
/// </summary>
internal static class AccountEndpoints
{
    internal const string AccountDeleted = "account.deleted";

    internal static void MapAccount(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/v1/auth").WithTags("Auth").AddEndpointFilter<RequestValidation>().RequireAuthorization();
        account.MapPost("/terms", AcceptTermsAsync)
            .WithSummary("Accepts the current version of the Terms of Service and the Privacy Policy.");
        account.MapGet("/account/export", ExportAsync)
            .WithSummary("Downloads everything stored about the signed-in user, as JSON.");
        account.MapDelete("/account", DeleteAsync).RequireRateLimiting(AuthSetup.AuthRateLimit)
            .WithSummary("Deletes the signed-in account after checking the password. Refused (409) while the user is the last "
                + "Owner of a workspace that still has other members, stations or a paid subscription.");
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> AcceptTermsAsync(AcceptTermsRequest request, HttpContext context,
        UserManager<AppUser> users, TimeProvider time)
    {
        if (request.Version != Legal.TermsVersion)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["version"] = [$"The current version is {Legal.TermsVersion}. Reload the page and read it first."],
            });
        }
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        user.AcceptTerms(Legal.TermsVersion, time.GetUtcNow());
        if (!(await users.UpdateAsync(user)).Succeeded)
        {
            // E.g. the account changed in another tab meanwhile: nothing was saved.
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Your account changed meanwhile. Try again.");
        }
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ExportAsync(HttpContext context, UserManager<AppUser> users, AppDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        // The user's data spans tenants, so the tenant filter is crossed here on purpose.
        var memberships = await (
                from m in db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter])
                join t in db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]) on m.TenantId equals t.Id
                where m.UserId == user.Id
                orderby t.Name
                select new AccountExport.MembershipData(t.Id, t.Name, m.Role, m.CreatedAt))
            .ToListAsync(cancellationToken);
        var devices = await db.DeviceSessions.Where(s => s.UserId == user.Id).OrderBy(s => s.CreatedAt)
            .Select(s => new AccountExport.DeviceData(s.DeviceName, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.RevokedAt))
            .ToListAsync(cancellationToken);
        var events = await db.SecurityEvents.Where(e => e.UserId == user.Id).OrderBy(e => e.OccurredAt)
            .Select(e => new AccountExport.SecurityEventData(e.Action, e.IpAddress, e.Detail, e.OccurredAt))
            .ToListAsync(cancellationToken);
        var audit = await db.AuditEntries.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(e => e.ActorUserId == user.Id)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new AccountExport.AuditData(e.TenantId, e.Action, e.TargetType, e.TargetId, e.Summary, e.OccurredAt))
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        var export = new AccountExport(now,
            new(user.Id, user.Email!, user.EmailConfirmed, user.CreatedAt, user.TermsVersion, user.TermsAcceptedAt),
            memberships, devices, events, audit);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"tropicast-account-{now:yyyyMMdd}.json\"";
        return TypedResults.Json(export);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync([FromBody] DeleteAccountRequest request, HttpContext context,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The password is incorrect.");
        }
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Not tracked: the database cascades them with the user (and with a deleted workspace), not EF.
        // Ordered: concurrent deletions lock the workspace rows below in the same order, so they cannot deadlock.
        var memberships = await db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking()
            .Where(m => m.UserId == user.Id).OrderBy(m => m.TenantId).ToListAsync(cancellationToken);
        var emptyTenants = new List<Guid>();
        foreach (var owned in memberships.Where(m => m.Role == MembershipRole.Owner))
        {
            // Locks the tenant row, as removing a member does: an Owner added meanwhile is seen.
            var tenant = await db.Tenants.FromSql($"SELECT *, xmin FROM tenants WHERE id = {owned.TenantId} FOR UPDATE")
                .IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking().SingleAsync(cancellationToken);
            var others = db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter])
                .Where(m => m.TenantId == tenant.Id && m.UserId != user.Id);
            if (await others.AnyAsync(m => m.Role == MembershipRole.Owner, cancellationToken))
            {
                continue;
            }
            if (await others.AnyAsync(cancellationToken))
            {
                return Conflict($"You are the last Owner of \"{tenant.Name}\". Invite another Owner or remove the other members first.");
            }
            if (await db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter, AppDbContext.DeletedFilter])
                    .AnyAsync(s => s.TenantId == tenant.Id && s.DeletedAt == null, cancellationToken))
            {
                return Conflict($"Delete the stations of \"{tenant.Name}\" first.");
            }
            if (await db.Subscriptions.IgnoreQueryFilters([AppDbContext.TenantFilter]).AnyAsync(s => s.TenantId == tenant.Id
                    && s.Status != SubscriptionStatus.Canceled, cancellationToken))
            {
                return Conflict($"Cancel the subscription of \"{tenant.Name}\" in the billing portal first.");
            }
            emptyTenants.Add(tenant.Id);
        }

        // Desktop devices: signed out, and the broadcast credentials they got for themselves revoked.
        await AuthEndpoints.RevokeAllDevicesAsync(db, user.Id, now, cancellationToken);

        // The audit log stays with the workspace, without the actor.
        await db.AuditEntries.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(e => e.ActorUserId == user.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.ActorUserId, (Guid?)null), cancellationToken);
        foreach (var membership in memberships.Where(m => !emptyTenants.Contains(m.TenantId)))
        {
            db.AuditEntries.Add(AuditEntry.Record(membership.TenantId, null, AccountDeleted, "Membership", membership.Id.ToString(),
                membership.Role.ToString(), now));
        }
        await db.SaveChangesAsync(cancellationToken);
        // A workspace the user alone used goes too, unless deleted stations keep it: their history stays for accounting,
        // with no member.
        var allStations = db.Stations.IgnoreQueryFilters([AppDbContext.TenantFilter, AppDbContext.DeletedFilter]);
        await db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(t => emptyTenants.Contains(t.Id) && !allStations.Any(s => s.TenantId == t.Id))
            .ExecuteDeleteAsync(cancellationToken);

        // Memberships, device sessions and requests, security events and Identity rows cascade with the user.
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Deleting the account failed: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
        await transaction.CommitAsync(cancellationToken);
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Conflict(string title) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}
