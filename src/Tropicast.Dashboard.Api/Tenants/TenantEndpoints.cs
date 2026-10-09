using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Tenants;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Tenants;
using Tropicast.Dashboard.Infrastructure;
using Tropicast.Dashboard.Infrastructure.Identity;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Tenants;

/// <summary>A plan's limits and features.</summary>
/// <param name="Id">Plan ID, e.g. "starter".</param>
/// <param name="Name">Display name.</param>
/// <param name="MaxStations">Stations the tenant may have.</param>
/// <param name="MaxListeners">Concurrent listeners per station.</param>
/// <param name="MaxBitrateKbps">Highest stream bitrate accepted.</param>
/// <param name="Formats">Stream formats: "mp3", "opus".</param>
/// <param name="DirectoryListing">RadioBrowser listing allowed.</param>
/// <param name="Embed">Embeddable player allowed.</param>
/// <param name="Analytics">Listener statistics included.</param>
internal sealed record PlanResponse(string Id, string Name, int MaxStations, int MaxListeners, int MaxBitrateKbps,
    IReadOnlyList<string> Formats, bool DirectoryListing, bool Embed, bool Analytics);

/// <summary>A tenant (customer account).</summary>
/// <param name="Id">Tenant ID; send it as X-Tenant-Id to choose this tenant.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL name, unique across Tropicast.</param>
/// <param name="Status">Active or Suspended.</param>
/// <param name="Plan">Current plan.</param>
/// <param name="StationCount">Stations in use, out of Plan.MaxStations.</param>
internal sealed record TenantResponse(Guid Id, string Name, string Slug, TenantStatus Status, PlanResponse Plan, int StationCount);

internal static class TenantEndpoints
{
    internal static void MapTenants(this IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/v1/tenants").WithTags("Tenants").AddEndpointFilter<CommandValidation>();
        tenants.MapPost("", CreateAsync).RequireAuthorization().WithETag()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates a tenant on the Free plan; the caller becomes its Owner.");
        tenants.MapGet("/current", GetAsync).RequireAuthorization(TenantPolicies.Member).WithETag()
            .WithSummary("The current tenant (X-Tenant-Id), its plan and usage.");
        tenants.MapPatch("/current", UpdateAsync).RequireAuthorization(TenantPolicies.Admin).WithIfMatch().WithETag()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .WithSummary("Renames the current tenant. Needs If-Match.");
    }

    private static async Task<Results<Created<TenantResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateTenantCommand command, HttpContext context, UserManager<AppUser> users, AppDbContext db, CurrentTenant current,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var tenant = Tenant.Create(command.Name, command.Slug, now);
        db.Tenants.Add(tenant);
        db.Memberships.Add(Membership.Create(tenant.Id, Guid.Parse(users.GetUserId(context.User)!), MembershipRole.Owner, now));
        if (await SaveAsync(db, cancellationToken) is { } problem)
        {
            return problem;
        }
        // Read back through the new tenant's scope.
        current.TenantId = tenant.Id;
        Concurrency.SetETag(context, db, tenant);
        return TypedResults.Created("/api/v1/tenants/current", await ResponseAsync(db, tenant, cancellationToken));
    }

    private static async Task<Ok<TenantResponse>> GetAsync(HttpContext context, AppDbContext db, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        Concurrency.SetETag(context, db, tenant);
        return TypedResults.Ok(await ResponseAsync(db, tenant, cancellationToken));
    }

    private static async Task<Results<Ok<TenantResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        UpdateTenantCommand command, HttpContext context, AppDbContext db, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        if (Concurrency.Check(context, db, tenant) is { } precondition)
        {
            return precondition;
        }
        tenant.Rename(command.Name ?? tenant.Name, command.Slug ?? tenant.Slug);
        if (await SaveAsync(db, cancellationToken) is { } problem)
        {
            return problem;
        }
        Concurrency.SetETag(context, db, tenant);
        return TypedResults.Ok(await ResponseAsync(db, tenant, cancellationToken));
    }

    private static async Task<ProblemHttpResult?> SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            return await Concurrency.SaveAsync(db, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another tenant uses this slug.");
        }
    }

    private static async Task<TenantResponse> ResponseAsync(AppDbContext db, Tenant tenant, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
        var stations = await db.Stations.CountAsync(cancellationToken);
        return new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, ToResponse(plan), stations);
    }

    internal static PlanResponse ToResponse(Plan plan) => new(plan.Id, plan.Name, plan.MaxStations, plan.MaxListeners,
        plan.MaxBitrateKbps,
        [.. new[] { (AudioFormat.Mp3, "mp3"), (AudioFormat.Opus, "opus") }.Where(f => plan.Allows(f.Item1)).Select(f => f.Item2)],
        plan.DirectoryListing, plan.Embed, plan.Analytics);
}
