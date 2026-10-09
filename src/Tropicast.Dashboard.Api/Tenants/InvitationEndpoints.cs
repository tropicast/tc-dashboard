using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Email;
using Tropicast.Dashboard.Application.Security;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Tenants;

internal sealed record InviteRequest(string Email, MembershipRole Role);
internal sealed record InvitationResponse(Guid Id, string Email, MembershipRole Role, DateTimeOffset ExpiresAt);
internal sealed record AcceptInvitationRequest(string Token);

/// <summary>Invite people to the current tenant by email; they accept after signing in with that address.</summary>
internal static class InvitationEndpoints
{
    private static readonly EmailAddressAttribute EmailFormat = new();

    internal static void MapInvitations(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/tenants/current/invitations", InviteAsync)
            .WithTags("Members")
            .RequireAuthorization(TenantPolicies.Admin)
            .WithSummary("Invites someone by email. Only an Owner can invite another Owner.");
        app.MapPost("/api/v1/invitations/accept", AcceptAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .WithSummary("Joins the tenant with the token from the invitation email.");
    }

    private static async Task<Results<Created<InvitationResponse>, ValidationProblem, ProblemHttpResult>> InviteAsync(
        InviteRequest request, HttpContext context, TenantAccess access, UserManager<AppUser> users, AppDbContext db,
        IEmailSender email, IOptions<AppOptions> app, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!EmailFormat.IsValid(request.Email) || request.Email.Length > 256)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] });
        }
        if (request.Role == MembershipRole.Owner && access.Role != MembershipRole.Owner)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an Owner can invite another Owner.");
        }
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        var token = Secrets.New();
        var invitation = Invitation.Create(tenant.Id, request.Email, request.Role, Secrets.Hash(token),
            Guid.Parse(users.GetUserId(context.User)!), time.GetUtcNow());
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        await email.SendAsync(AuthEmails.Invitation(invitation.Email, app.Value.PublicBaseUrl, tenant.Name, request.Role.ToString(), token),
            cancellationToken);
        return TypedResults.Created($"/api/v1/tenants/current/invitations/{invitation.Id}",
            new InvitationResponse(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AcceptAsync(AcceptInvitationRequest request,
        HttpContext context, UserManager<AppUser> users, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hash = Secrets.Hash(request.Token ?? "");
        // The accepting user is not a member yet, so the tenant filter is crossed here on purpose.
        var invitation = await db.Invitations.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitation is null || !invitation.IsOpen(now))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "The invitation was not found, was already used, or has expired.");
        }
        var user = await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Signed-in user not found.");
        if (!user.EmailConfirmed || !invitation.IsFor(user.Email!))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This invitation is for another email address.");
        }
        if (await db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter])
                .AnyAsync(m => m.TenantId == invitation.TenantId && m.UserId == user.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "You are already a member of this tenant.");
        }
        db.Memberships.Add(invitation.Accept(user.Id, now));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
