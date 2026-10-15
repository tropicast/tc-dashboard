using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Application.Media;
using Tropicast.Dashboard.Domain.Media;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api.Media;

/// <summary>Where and how the browser sends the file.</summary>
/// <param name="TrackId">Confirm the upload with it (<c>uploadId</c>).</param>
/// <param name="UploadUrl">Presigned bucket URL: PUT the file's bytes there.</param>
/// <param name="Headers">Headers the PUT must carry, exactly (the URL is signed for them).</param>
/// <param name="ExpiresAt">The URL and the pending upload end then.</param>
internal sealed record TrackUploadResponse(Guid TrackId, Uri UploadUrl, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

/// <summary>An uploaded track.</summary>
/// <param name="Id">Track ID.</param>
/// <param name="FileName">The uploaded file's name.</param>
/// <param name="Title">Shown in playlists.</param>
/// <param name="Artist">May be empty.</param>
/// <param name="Format">Read from the file: Mp3, Flac, OggOpus or OggVorbis.</param>
/// <param name="DurationMs">Playing time.</param>
/// <param name="SizeBytes">Stored size.</param>
/// <param name="CreatedAt">When the upload started.</param>
internal sealed record TrackResponse(Guid Id, string FileName, string Title, string Artist, TrackFormat Format, long DurationMs,
    long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>Audio storage of the tenant, across its stations.</summary>
/// <param name="UsedBytes">Stored tracks and uploads in progress.</param>
/// <param name="QuotaBytes">The plan's quota; 0 when the plan has no AutoDJ.</param>
internal sealed record StorageUsageResponse(long UsedBytes, long QuotaBytes);

/// <summary>A station's tracks and the tenant's storage use.</summary>
/// <param name="Tracks">Confirmed tracks, newest first.</param>
/// <param name="Storage">Used and allowed bytes.</param>
internal sealed record TrackLibraryResponse(IReadOnlyList<TrackResponse> Tracks, StorageUsageResponse Storage);

/// <summary>
/// The media library of a station (#104): upload in two steps (a presigned PUT, then a confirm the server checks
/// against the stored object), list and delete. Stations of other tenants answer 404.
/// </summary>
internal static class TrackEndpoints
{
    internal static void MapTracks(this IEndpointRouteBuilder app)
    {
        var tracks = app.MapGroup("/api/v1/stations/{stationId:guid}/tracks").WithTags("Media library")
            .AddEndpointFilter<CommandValidation>().RequireAuthorization(TenantPolicies.Member);
        tracks.MapGet("", ListAsync).WithSummary("The station's tracks and the tenant's audio storage use.");
        tracks.MapPost("/uploads", StartUploadAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Starts an upload: PUT the file to the returned URL with the returned headers, then confirm it.");
        tracks.MapPost("", ConfirmAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Confirms an upload: the server reads the stored file's format, duration and size, then keeps or deletes it.");
        tracks.MapDelete("/{trackId:guid}", DeleteAsync).WithSummary("Deletes the track and its stored file.");
    }

    private static async Task<Results<Ok<TrackLibraryResponse>, NotFound>> ListAsync(Guid stationId, AppDbContext db,
        TimeProvider time, CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var tracks = await db.Tracks.Where(t => t.StationId == stationId && t.Status == TrackStatus.Ready)
            .OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);
        var plan = await CurrentPlanAsync(db, cancellationToken);
        return TypedResults.Ok(new TrackLibraryResponse([.. tracks.Select(ToResponse)],
            new StorageUsageResponse(await UsedBytesAsync(db, time.GetUtcNow(), null, cancellationToken), plan.StorageQuotaBytes)));
    }

    private static async Task<Results<Ok<TrackUploadResponse>, NotFound, ProblemHttpResult>> StartUploadAsync(Guid stationId,
        StartTrackUploadCommand command, AppDbContext db, TenantAccess access, IObjectStorage storage, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Stations.AnyAsync(s => s.Id == stationId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        if (!storage.IsConfigured)
        {
            return Unavailable();
        }
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Locks the tenant row: concurrent uploads cannot both pass the quota.
        var plan = await LockPlanAsync(db, access, cancellationToken);
        if (!plan.AutoDj)
        {
            return NoAutoDj(plan);
        }
        if (!TrackRules.FitsQuota(plan, await UsedBytesAsync(db, now, null, cancellationToken), command.Size))
        {
            return OverQuota(plan);
        }
        var track = Track.StartUpload(stationId, Path.GetFileName(command.FileName), command.Size, now, TrackRules.UploadLifetime);
        db.Tracks.Add(track);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var contentType = TrackRules.ContentTypeOf(command.FileName)!;
        return TypedResults.Ok(new TrackUploadResponse(track.Id, storage.PresignUpload(track.ObjectKey, contentType, TrackRules.UploadLifetime),
            new Dictionary<string, string> { ["Content-Type"] = contentType }, track.UploadExpiresAt!.Value));
    }

    private static async Task<Results<Created<TrackResponse>, NotFound, ProblemHttpResult>> ConfirmAsync(Guid stationId,
        ConfirmTrackCommand command, AppDbContext db, TenantAccess access, IObjectStorage storage, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured)
        {
            return Unavailable();
        }
        var now = time.GetUtcNow();
        var track = await db.Tracks.SingleOrDefaultAsync(t => t.Id == command.UploadId && t.StationId == stationId
            && t.Status == TrackStatus.Pending && t.UploadExpiresAt > now, cancellationToken);
        if (track is null)
        {
            return TypedResults.NotFound();
        }
        var stored = await storage.HeadAsync(track.ObjectKey, cancellationToken);
        if (stored is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "The file has not arrived in storage. Upload it to the URL you were given, then confirm again.");
        }
        if (stored.Size > TrackRules.MaxBytes)
        {
            return await RefuseAsync(db, storage, track, StatusCodes.Status422UnprocessableEntity,
                $"Files up to {TrackRules.Size(TrackRules.MaxBytes)} are accepted.", cancellationToken);
        }

        var head = await storage.ReadRangeAsync(track.ObjectKey, 0, (int)Math.Min(stored.Size, AudioProbe.HeadBytes), cancellationToken);
        var tailLength = (int)Math.Min(stored.Size, AudioProbe.TailBytes);
        var tail = stored.Size <= AudioProbe.HeadBytes ? head
            : await storage.ReadRangeAsync(track.ObjectKey, stored.Size - tailLength, tailLength, cancellationToken);
        var info = AudioProbe.Read(head, tail, stored.Size);
        if (info is null)
        {
            return await RefuseAsync(db, storage, track, StatusCodes.Status422UnprocessableEntity,
                $"This file is not {TrackRules.Accepted} audio the server can read.", cancellationToken);
        }
        if (info.Duration > TrackRules.MaxDuration)
        {
            return await RefuseAsync(db, storage, track, StatusCodes.Status422UnprocessableEntity,
                $"Tracks up to {TrackRules.MaxDuration.TotalHours:0} hours are accepted.", cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var plan = await LockPlanAsync(db, access, cancellationToken);
        // The real size counts now, not the declared one: a larger file than announced must still fit.
        if (!plan.AutoDj || !TrackRules.FitsQuota(plan, await UsedBytesAsync(db, now, track.Id, cancellationToken), stored.Size))
        {
            await transaction.RollbackAsync(cancellationToken);
            return await RefuseAsync(db, storage, track, StatusCodes.Status403Forbidden,
                plan.AutoDj ? TrackRules.QuotaMessage(plan) : TrackRules.NoAutoDjMessage(plan), cancellationToken);
        }
        var fallbackTitle = Path.GetFileNameWithoutExtension(track.FileName);
        track.Confirm(info.Format, info.Duration, stored.Size,
            FirstNonEmpty(command.Title, info.Title, fallbackTitle), FirstNonEmpty(command.Artist, info.Artist));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/stations/{stationId}/tracks/{track.Id}", ToResponse(track));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid stationId, Guid trackId, AppDbContext db,
        IObjectStorage storage, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.SingleOrDefaultAsync(t => t.Id == trackId && t.StationId == stationId, cancellationToken);
        if (track is null)
        {
            return TypedResults.NotFound();
        }
        // The object first: a failure leaves the row, so the delete can be retried.
        if (storage.IsConfigured)
        {
            await storage.DeleteAsync(track.ObjectKey, cancellationToken);
        }
        db.Tracks.Remove(track);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Refused after the upload: nothing stays in the bucket, and the pending track goes.</summary>
    private static async Task<ProblemHttpResult> RefuseAsync(AppDbContext db, IObjectStorage storage, Track track, int status,
        string title, CancellationToken cancellationToken)
    {
        await storage.DeleteAsync(track.ObjectKey, cancellationToken);
        db.ChangeTracker.Clear();
        await db.Tracks.Where(t => t.Id == track.Id).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.Problem(statusCode: status, title: title);
    }

    /// <summary>Bytes the tenant stores: confirmed tracks and uploads still pending, except <paramref name="except"/>.</summary>
    internal static Task<long> UsedBytesAsync(AppDbContext db, DateTimeOffset now, Guid? except, CancellationToken cancellationToken)
        => db.Tracks.Where(t => t.Id != except && (t.Status == TrackStatus.Ready || t.UploadExpiresAt > now))
            .SumAsync(t => t.SizeBytes, cancellationToken);

    private static async Task<Plan> LockPlanAsync(AppDbContext db, TenantAccess access, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FromSql($"SELECT *, xmin FROM tenants WHERE id = {access.TenantId} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);
        return await db.Plans.SingleAsync(p => p.Id == tenant.PlanId, cancellationToken);
    }

    private static async Task<Plan> CurrentPlanAsync(AppDbContext db, CancellationToken cancellationToken)
        => await db.Tenants.Join(db.Plans, t => t.PlanId, p => p.Id, (_, p) => p).SingleAsync(cancellationToken);

    private static ProblemHttpResult NoAutoDj(Plan plan) => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
        title: TrackRules.NoAutoDjMessage(plan), extensions: new Dictionary<string, object?> { ["limit"] = "autodj" });

    private static ProblemHttpResult OverQuota(Plan plan) => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
        title: TrackRules.QuotaMessage(plan),
        extensions: new Dictionary<string, object?> { ["limit"] = "storage", ["max"] = plan.StorageQuotaBytes });

    private static ProblemHttpResult Unavailable() => TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Audio storage is not available right now. Try again later.");

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    internal static TrackResponse ToResponse(Track track) => new(track.Id, track.FileName, track.Title, track.Artist,
        track.Format ?? TrackFormat.Mp3, track.DurationMs ?? 0, track.SizeBytes, track.CreatedAt);
}
