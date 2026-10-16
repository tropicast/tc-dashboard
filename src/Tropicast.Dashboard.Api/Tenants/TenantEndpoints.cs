using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Email;
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
/// <param name="AutoDj">AutoDJ: uploaded tracks play when no DJ is live.</param>
/// <param name="AutoDjScheduling">Scheduled AutoDJ playlists; without it, one fallback playlist.</param>
/// <param name="StorageQuotaBytes">Uploaded audio the tenant may store, across its stations.</param>
internal sealed record PlanResponse(string Id, string Name, int MaxStations, int MaxListeners, int MaxBitrateKbps,
    IReadOnlyList<string> Formats, bool DirectoryListing, bool Embed, bool Analytics, bool AutoDj, bool AutoDjScheduling,
    long StorageQuotaBytes);

/// <summary>A tenant (customer account).</summary>
/// <param name="Id">Tenant ID; send it as X-Tenant-Id to choose this tenant.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL name, unique across Tropicast.</param>
/// <param name="Status">Active or Suspended.</param>
/// <param name="Plan">Current plan.</param>
/// <param name="StationCount">Stations in use, out of Plan.MaxStations.</param>
/// <param name="StorageUsedBytes">Uploaded audio stored, out of Plan.StorageQuotaBytes.</param>
internal sealed record TenantResponse(Guid Id, string Name, string Slug, TenantStatus Status, PlanResponse Plan, int StationCount,
    long StorageUsedBytes);

/// <summary>Confirms deleting the current workspace.</summary>
/// <param name="ConfirmSlug">The workspace's slug, typed by the user.</param>
internal sealed record DeleteWorkspaceRequest([property: Required, MaxLength(64)] string ConfirmSlug);

/// <summary>The workspace is gone.</summary>
/// <param name="Counts">What was deleted, per kind.</param>
internal sealed record WorkspaceDeletedResponse(DeletionCountsResponse Counts);

internal static class TenantEndpoints
{
    internal const string WorkspaceDeleted = "workspace.deleted";

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
        tenants.MapGet("/current/deletion", GetDeletionAsync).RequireAuthorization(TenantPolicies.Owner)
            .WithSummary("What deleting the current workspace would delete, and what blocks it. Changes nothing.");
        tenants.MapDelete("/current", DeleteAsync).RequireAuthorization(TenantPolicies.Owner).AddEndpointFilter<RequestValidation>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Deletes the current workspace and everything in it; member accounts stay. Needs its slug. Refused (409) "
                + "while it has a subscription Stripe may charge for, or a live station.");
    }

    private static async Task<Ok<WorkspaceDeletionResponse>> GetDeletionAsync(AppDbContext db, CancellationToken cancellationToken)
        => TypedResults.Ok(await WorkspaceDeletion.ReportAsync(db, await db.Tenants.SingleAsync(cancellationToken), cancellationToken));

    private static async Task<Results<Ok<WorkspaceDeletedResponse>, ProblemHttpResult>> DeleteAsync([FromBody] DeleteWorkspaceRequest request,
        HttpContext context, UserManager<AppUser> users, AppDbContext db, IEmailSender email, ILoggerFactory loggers, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(cancellationToken);
        if (request.ConfirmSlug.Trim() != tenant.Slug)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Type the workspace's slug exactly to confirm.");
        }
        var owners = await db.Memberships.Where(m => m.Role == MembershipRole.Owner)
            .Join(db.Users, m => m.UserId, u => u.Id, (_, u) => u.Email!).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (blockers, counts) = await WorkspaceDeletion.DeleteAsync(db, tenant.Id, now, cancellationToken);
        if (counts is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: blockers[0] switch
            {
                DeletionBlocker.Subscription => "Cancel the subscription in the billing portal first.",
                _ => "A station is live. Stop the broadcast first.",
            });
        }
        // Operator record, kept with the user's security events: no member emails, no station names.
        SecurityLog.Record(db, Guid.Parse(users.GetUserId(context.User)!), WorkspaceDeleted, context,
            $"{tenant.Id} ({tenant.Slug}): {counts.Stations} stations, {counts.Tracks} tracks, {counts.Members} members", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var logger = loggers.CreateLogger(typeof(TenantEndpoints));
        LogDeleted(logger, tenant.Id, counts.Stations, counts.Members);
        foreach (var owner in owners)
        {
            try
            {
                await email.SendAsync(DeletedEmail(owner, tenant.Name, counts), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The workspace is gone either way: a lost confirmation must not turn the deletion into an error.
                LogEmailFailed(logger, ex.GetType().Name);
            }
        }
        return TypedResults.Ok(new WorkspaceDeletedResponse(counts));
    }

    internal static EmailMessage DeletedEmail(string to, string tenantName, DeletionCountsResponse counts) => new(to,
        $"Your Tropicast workspace {tenantName} was deleted",
        $"""
        The workspace {tenantName} was deleted from Tropicast, with its {counts.Stations} station(s), their uploaded audio and
        their history. Member accounts stay: their other workspaces are not affected.

        Invoices stay in Stripe, as accounting law requires. Database backups keep a copy for up to 30 days.

        If you did not ask for this, reply to this email.
        """);

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
        return TypedResults.Created("/api/v1/tenants/current", await ResponseAsync(db, tenant, time.GetUtcNow(), cancellationToken));
    }

    private static async Task<Ok<TenantResponse>> GetAsync(HttpContext context, AppDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        Concurrency.SetETag(context, db, tenant);
        return TypedResults.Ok(await ResponseAsync(db, tenant, time.GetUtcNow(), cancellationToken));
    }

    private static async Task<Results<Ok<TenantResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        UpdateTenantCommand command, HttpContext context, AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
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
        return TypedResults.Ok(await ResponseAsync(db, tenant, time.GetUtcNow(), cancellationToken));
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

    private static async Task<TenantResponse> ResponseAsync(AppDbContext db, Tenant tenant, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var plan = await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
        var stations = await db.Stations.CountAsync(cancellationToken);
        var storage = await Media.TrackEndpoints.UsedBytesAsync(db, now, null, cancellationToken);
        return new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, ToResponse(plan), stations, storage);
    }

    private static readonly Action<ILogger, Guid, int, int, Exception?> Deleted = LoggerMessage.Define<Guid, int, int>(LogLevel.Information,
        new EventId(1250, "WorkspaceDeleted"), "Workspace {TenantId} deleted by its owner ({Stations} stations, {Members} members)");

    private static readonly Action<ILogger, string, Exception?> EmailFailed = LoggerMessage.Define<string>(LogLevel.Warning,
        new EventId(1251, "WorkspaceDeletedEmailFailed"), "Workspace deletion email not sent ({ErrorType})");

    private static void LogDeleted(ILogger logger, Guid tenantId, int stations, int members) => Deleted(logger, tenantId, stations, members, null);

    private static void LogEmailFailed(ILogger logger, string errorType) => EmailFailed(logger, errorType, null);

    internal static PlanResponse ToResponse(Plan plan) => new(plan.Id, plan.Name, plan.MaxStations, plan.MaxListeners,
        plan.MaxBitrateKbps,
        [.. new[] { (AudioFormat.Mp3, "mp3"), (AudioFormat.Opus, "opus") }.Where(f => plan.Allows(f.Item1)).Select(f => f.Item2)],
        plan.DirectoryListing, plan.Embed, plan.Analytics, plan.AutoDj, plan.AutoDjScheduling, plan.StorageQuotaBytes);
}
