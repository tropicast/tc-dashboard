using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Auth;

/// <summary>The caller's membership in the tenant of this request, resolved once per request.</summary>
internal sealed class TenantAccess
{
    /// <summary>Selects the tenant; optional when the user belongs to exactly one.</summary>
    internal const string Header = "X-Tenant-Id";

    public Guid? TenantId { get; private set; }
    public MembershipRole? Role { get; private set; }

    internal void Set(Membership membership)
    {
        TenantId = membership.TenantId;
        Role = membership.Role;
    }
}

/// <summary>
/// Resolves <see cref="TenantAccess"/> from the <see cref="TenantAccess.Header"/> header and the user's memberships,
/// and scopes the database's tenant filter to it. No membership: no tenant, so tenant data stays invisible.
/// </summary>
internal sealed class TenantAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Services are resolved only for signed-in API calls: /health and the SPA must not need the database.
        if (context.Request.Path.StartsWithSegments("/api") && context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.RequestServices.GetRequiredService<UserManager<AppUser>>().GetUserId(context.User), out var userId))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var memberships = db.Memberships.IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(m => m.UserId == userId);
            var requested = context.Request.Headers[TenantAccess.Header].ToString();
            var membership = Guid.TryParse(requested, out var tenantId)
                ? await memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId, context.RequestAborted)
                : requested.Length == 0 && await memberships.Take(2).ToListAsync(context.RequestAborted) is [var only] ? only : null;
            if (membership is not null)
            {
                context.RequestServices.GetRequiredService<TenantAccess>().Set(membership);
                context.RequestServices.GetRequiredService<CurrentTenant>().TenantId = membership.TenantId;
            }
        }
        await next(context);
    }
}

/// <summary>Policy names. Station endpoints use these, never ad-hoc role checks.</summary>
public static class TenantPolicies
{
    /// <summary>Any member: read the tenant and its stations.</summary>
    public const string Member = "tenant.member";
    /// <summary>Any member: publish audio to the tenant's stations.</summary>
    public const string Broadcaster = "station.broadcaster";
    /// <summary>Owner or Admin: manage stations, credentials and members.</summary>
    public const string Admin = "tenant.admin";
    /// <summary>Owner only: billing, plan and ownership.</summary>
    public const string Owner = "tenant.owner";

    internal static AuthorizationPolicy Require(params MembershipRole[] roles)
        => new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new TenantRoleRequirement(roles.ToHashSet())).Build();
}

internal sealed record TenantRoleRequirement(IReadOnlySet<MembershipRole> Roles) : IAuthorizationRequirement;

/// <summary>The one tenant-role check behind every tenant policy.</summary>
internal sealed class TenantRoleHandler(TenantAccess access) : AuthorizationHandler<TenantRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantRoleRequirement requirement)
    {
        if (access.Role is { } role && requirement.Roles.Contains(role))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
