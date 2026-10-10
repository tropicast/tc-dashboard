using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Application.Stations;
using Tropicast.Dashboard.Domain;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Stations;

/// <summary>Listener URLs of a station. <see cref="Opus"/> is null when the plan has no Opus.</summary>
/// <param name="Mp3">MP3 stream, playable everywhere.</param>
/// <param name="Opus">Ogg Opus stream: about half the mobile data of MP3.</param>
internal sealed record ListenerUrls(Uri Mp3, Uri? Opus);

/// <summary>A station.</summary>
/// <param name="Id">ID in the API.</param>
/// <param name="PublicId">ID in listener URLs and Icecast mounts; never changes.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL name, unique in the tenant.</param>
/// <param name="Description">Shown on the public page and in directories.</param>
/// <param name="Genre">Music or programme genre.</param>
/// <param name="Country">ISO 3166-1 alpha-2 code.</param>
/// <param name="Language">BCP 47 language tag.</param>
/// <param name="LogoUrl">Logo address.</param>
/// <param name="Website">The station's website.</param>
/// <param name="ListInDirectory">Listed on RadioBrowser (only when the plan includes it).</param>
/// <param name="CreatedAt">When the station was created.</param>
/// <param name="ListenerUrls">Where listeners play the station.</param>
internal sealed record StationResponse(Guid Id, string PublicId, string Name, string Slug, string Description, string Genre,
    string? Country, string? Language, Uri? LogoUrl, Uri? Website, bool ListInDirectory, DateTimeOffset CreatedAt,
    ListenerUrls ListenerUrls);

/// <summary>
/// Stations of the current tenant. Stations of other tenants are invisible (404) through the tenant filter.
/// Updates need <c>If-Match</c>; changes are published through the outbox.
/// </summary>
internal static partial class StationEndpoints
{
    internal static void MapStations(this IEndpointRouteBuilder app)
    {
        var stations = app.MapGroup("/api/v1/stations").WithTags("Stations").AddEndpointFilter<CommandValidation>();
        stations.MapGet("", ListAsync).RequireAuthorization(TenantPolicies.Member)
            .WithSummary("Stations of the current tenant, newest first.");
        stations.MapPost("", CreateAsync).RequireAuthorization(TenantPolicies.Admin).WithETag()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Creates a station within the plan's station limit and assigns it a stream.");
        stations.MapGet("/{id:guid}", GetAsync).RequireAuthorization(TenantPolicies.Member).WithETag()
            .WithSummary("One station; the ETag header is needed to change it.");
        stations.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization(TenantPolicies.Admin).WithIfMatch().WithETag()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .WithSummary("Changes station settings. Needs If-Match.");
        stations.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(TenantPolicies.Admin).WithIfMatch(required: false)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .WithSummary("Deletes the station; its history is kept and its public ID is never reused.");
    }

    private static async Task<Results<Ok<PagedList<StationResponse>>, ValidationProblem>> ListAsync(AppDbContext db,
        IOptions<StreamingOptions> streaming, CancellationToken cancellationToken, int page = 1, int pageSize = Paging.DefaultSize)
    {
        if (Paging.Validate(page, pageSize) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }
        var plan = await CurrentPlanAsync(db, cancellationToken);
        var query = db.Stations.OrderByDescending(s => s.CreatedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .GroupJoin(db.StreamAssignments, s => s.Id, a => a.StationId, (s, a) => new { Station = s, Assignment = a.FirstOrDefault() })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new PagedList<StationResponse>(
            [.. items.Select(i => ToResponse(i.Station, i.Assignment, plan, streaming.Value))], page, pageSize, total));
    }

    private static async Task<Results<Created<StationResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateStationCommand command, HttpContext context, AppDbContext db, TenantAccess access,
        IOptions<StreamingOptions> streaming, TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var logger = loggers.CreateLogger(typeof(StationEndpoints));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Locks the tenant row: concurrent creates cannot both pass the station limit.
        var tenant = await db.Tenants.FromSql($"SELECT *, xmin FROM tenants WHERE id = {access.TenantId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var plan = await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
        if (!StationQuota.AllowsAnother(plan, await db.Stations.CountAsync(cancellationToken)))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: StationQuota.LimitMessage(plan),
                extensions: new Dictionary<string, object?> { ["limit"] = "stations", ["max"] = plan.MaxStations });
        }
        if (await NodeFullAsync(db, streaming.Value, plan, logger, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "No streaming capacity is free right now. The operator has been alerted; try again later.");
        }

        var station = Station.Create(tenant.Id, command.Name, command.Slug, now);
        station.Describe(command.Description, command.Genre, command.Country, command.Language, command.LogoUrl, command.Website, now);
        station.SetDirectoryListing(command.ListInDirectory, plan, now);
        var assignment = StreamAssignment.Create(station, streaming.Value.Node, now);
        db.Stations.Add(station);
        db.StreamAssignments.Add(assignment);
        if (await SaveAsync(db, cancellationToken) is { } conflict)
        {
            return conflict;
        }
        await transaction.CommitAsync(cancellationToken);
        Concurrency.SetETag(context, db, station);
        return TypedResults.Created($"/api/v1/stations/{station.Id}", ToResponse(station, assignment, plan, streaming.Value));
    }

    private static async Task<Results<Ok<StationResponse>, NotFound>> GetAsync(Guid id, HttpContext context, AppDbContext db,
        IOptions<StreamingOptions> streaming, CancellationToken cancellationToken)
    {
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        Concurrency.SetETag(context, db, station);
        return TypedResults.Ok(await ResponseAsync(db, station, streaming.Value, cancellationToken));
    }

    private static async Task<Results<Ok<StationResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(Guid id,
        UpdateStationCommand command, HttpContext context, AppDbContext db, IOptions<StreamingOptions> streaming, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(context, db, station) is { } precondition)
        {
            return precondition;
        }
        var now = time.GetUtcNow();
        if (command.Name is not null || command.Slug is not null)
        {
            station.Rename(command.Name ?? station.Name, command.Slug ?? station.Slug, now);
        }
        if (command is { Description: not null } or { Genre: not null } or { Country: not null } or { Language: not null }
            or { LogoUrl: not null } or { Website: not null })
        {
            station.Describe(command.Description ?? station.Description, command.Genre ?? station.Genre,
                command.Country ?? station.Country, command.Language ?? station.Language,
                command.LogoUrl ?? station.LogoUrl, command.Website ?? station.Website, now);
        }
        if (command.ListInDirectory is { } listed)
        {
            station.SetDirectoryListing(listed, await CurrentPlanAsync(db, cancellationToken), now);
        }
        if (await SaveAsync(db, cancellationToken) is { } failed)
        {
            return failed;
        }
        Concurrency.SetETag(context, db, station);
        return TypedResults.Ok(await ResponseAsync(db, station, streaming.Value, cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(Guid id, HttpContext context,
        AppDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(context, db, station, required: false) is { } precondition)
        {
            return precondition;
        }
        station.Delete(time.GetUtcNow());
        return await Concurrency.SaveAsync(db, cancellationToken) is { } stale ? stale : TypedResults.NoContent();
    }

    /// <summary>
    /// Whether one more station on this plan would overload the node. Locks the node for the transaction, so
    /// concurrent creates in different tenants cannot overload it together.
    /// </summary>
    private static async Task<bool> NodeFullAsync(AppDbContext db, StreamingOptions streaming, Plan plan, ILogger logger,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"node:" + streaming.Node}))", cancellationToken);
        var assigned = await db.StreamAssignments.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(a => a.Node == streaming.Node && a.Station.DeletedAt == null)
            .Join(db.Tenants.IgnoreQueryFilters([AppDbContext.TenantFilter]), a => a.Station.TenantId, t => t.Id, (_, t) => t.PlanId)
            .Join(db.Plans, planId => planId, p => p.Id, (_, p) => p)
            .ToListAsync(cancellationToken);
        var reason = NodeCapacity.WhyNot(assigned, plan, new NodeLimits(streaming.MaxSources, streaming.MaxListenerCaps));
        if (reason is not null)
        {
            LogNodeFull(logger, streaming.Node, reason);
        }
        return reason is not null;
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Streaming node {Node} is full: {Reason}. Add a node or raise its limits.")]
    private static partial void LogNodeFull(ILogger logger, string node, string reason);

    /// <summary>Saves; a duplicate slug answers 409, a concurrent change 412.</summary>
    private static async Task<ProblemHttpResult?> SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            return await Concurrency.SaveAsync(db, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another station of this tenant uses this slug.");
        }
    }

    private static async Task<Plan> CurrentPlanAsync(AppDbContext db, CancellationToken cancellationToken)
        => await db.Tenants.Join(db.Plans, t => t.PlanId, p => p.Id, (_, p) => p).SingleAsync(cancellationToken);

    private static async Task<StationResponse> ResponseAsync(AppDbContext db, Station station, StreamingOptions streaming,
        CancellationToken cancellationToken)
    {
        var assignment = await db.StreamAssignments.SingleOrDefaultAsync(a => a.StationId == station.Id, cancellationToken);
        return ToResponse(station, assignment, await CurrentPlanAsync(db, cancellationToken), streaming);
    }

    private static StationResponse ToResponse(Station station, StreamAssignment? assignment, Plan plan, StreamingOptions streaming)
    {
        var mountBase = assignment?.MountBase ?? $"/stations/{station.PublicId}";
        var mp3 = new Uri(streaming.ListenerBaseUrl, $"{mountBase}/live.mp3");
        var opus = plan.Allows(AudioFormat.Opus) ? new Uri(streaming.ListenerBaseUrl, $"{mountBase}/live.opus") : null;
        return new StationResponse(station.Id, station.PublicId, station.Name, station.Slug, station.Description, station.Genre,
            station.Country, station.Language, station.LogoUrl, station.Website, station.ListInDirectory, station.CreatedAt,
            new ListenerUrls(mp3, opus));
    }
}
