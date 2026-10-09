using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Tenants;

/// <summary>A member of the current tenant.</summary>
/// <param name="UserId">The member's account ID.</param>
/// <param name="Email">The member's email address.</param>
/// <param name="Role">Owner, Admin or Broadcaster.</param>
/// <param name="JoinedAt">When the member joined.</param>
internal sealed record MemberResponse(Guid UserId, string Email, MembershipRole Role, DateTimeOffset JoinedAt);

/// <summary>Members of the current tenant. New members join by invitation (POST /tenants/current/invitations).</summary>
internal static class MemberEndpoints
{
    internal static void MapMembers(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/v1/tenants/current/members").WithTags("Members");
        members.MapGet("", ListAsync).RequireAuthorization(TenantPolicies.Member).WithSummary("Members of the current tenant.");
        members.MapDelete("/{userId:guid}", RemoveAsync).RequireAuthorization(TenantPolicies.Admin)
            .WithSummary("Removes a member. Only an Owner removes an Owner; the last Owner stays.");
    }

    private static async Task<Ok<List<MemberResponse>>> ListAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var members = await (
                from m in db.Memberships
                join u in db.Users on m.UserId equals u.Id
                orderby m.CreatedAt
                select new { m.UserId, u.Email, m.Role, m.CreatedAt })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(members.Select(m => new MemberResponse(m.UserId, m.Email!, m.Role, m.CreatedAt)).ToList());
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(Guid userId, TenantAccess access,
        AppDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Locks the tenant row: two concurrent removals cannot both see a second Owner and leave none.
        await db.Tenants.FromSql($"SELECT *, xmin FROM tenants WHERE id = {access.TenantId} FOR UPDATE").SingleAsync(cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }
        if (membership.Role == MembershipRole.Owner)
        {
            if (access.Role != MembershipRole.Owner)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an Owner can remove an Owner.");
            }
            if (await db.Memberships.CountAsync(m => m.Role == MembershipRole.Owner, cancellationToken) == 1)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The last Owner cannot be removed. Make another member Owner first.");
            }
        }
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
