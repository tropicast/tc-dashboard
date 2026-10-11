using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Tenants;

/// <summary>An audit log entry.</summary>
/// <param name="Action">What happened, e.g. <c>credential.created</c>, <c>member.removed</c>, <c>plan.changed</c>.</param>
/// <param name="ActorEmail">Who did it; null for the system (billing, a background job) or a deleted account.</param>
/// <param name="TargetType">Kind of object.</param>
/// <param name="TargetId">Its ID.</param>
/// <param name="Summary">Short context, never a secret.</param>
/// <param name="OccurredAt">When.</param>
internal sealed record AuditEntryResponse(string Action, string? ActorEmail, string TargetType, string TargetId, string? Summary,
    DateTimeOffset OccurredAt);

/// <summary>The tenant's audit log (#16): stations, credentials, members, plan.</summary>
internal static class AuditEndpoints
{
    internal static void MapAudit(this IEndpointRouteBuilder app)
        => app.MapGet("/api/v1/tenants/current/audit", ListAsync).WithTags("Tenants").RequireAuthorization(TenantPolicies.Admin)
            .WithSummary("Security-relevant actions in the tenant, newest first.");

    private static async Task<Results<Ok<PagedList<AuditEntryResponse>>, ValidationProblem>> ListAsync(AppDbContext db,
        CancellationToken cancellationToken, int page = 1, int pageSize = Paging.DefaultSize)
    {
        if (Paging.Validate(page, pageSize) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }
        var total = await db.AuditEntries.CountAsync(cancellationToken);
        var items = await db.AuditEntries
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .GroupJoin(db.Users, e => e.ActorUserId, u => u.Id, (e, u) => new { Entry = e, Actor = u.Select(x => x.Email).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedList<AuditEntryResponse>([.. items.Select(i => new AuditEntryResponse(i.Entry.Action, i.Actor,
            i.Entry.TargetType, i.Entry.TargetId, i.Entry.Summary, i.Entry.OccurredAt))], page, pageSize, total));
    }
}
