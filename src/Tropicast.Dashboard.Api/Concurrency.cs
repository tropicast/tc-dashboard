using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tropicast.Dashboard.Infrastructure.Persistence;

namespace Tropicast.Dashboard.Api;

/// <summary>
/// ETags from PostgreSQL's <c>xmin</c> (the <c>Version</c> shadow property): every update changes it.
/// Updates require <c>If-Match</c> with the ETag the client last read.
/// </summary>
internal static class Concurrency
{
    internal static string ETag(AppDbContext db, object entity)
        => $"\"{db.Entry(entity).Property("Version").CurrentValue}\"";

    internal static void SetETag(HttpContext context, AppDbContext db, object entity)
        => context.Response.Headers.ETag = ETag(db, entity);

    /// <summary>
    /// Checks <c>If-Match</c> against the entity and makes the save fail if the row changes meanwhile.
    /// Returns a problem (428 missing, 412 stale) or null to proceed.
    /// </summary>
    internal static ProblemHttpResult? Check(HttpContext context, AppDbContext db, object entity, bool required = true)
    {
        var ifMatch = context.Request.Headers.IfMatch.ToString().Trim();
        if (ifMatch.Length == 0)
        {
            return required
                ? TypedResults.Problem(statusCode: StatusCodes.Status428PreconditionRequired,
                    title: "Send If-Match with the ETag from your last read of this resource.")
                : null;
        }
        var version = db.Entry(entity).Property("Version");
        if (ifMatch != "*" && ifMatch != ETag(db, entity))
        {
            return Stale();
        }
        // The UPDATE then also checks xmin, so a change between this read and the save is caught too.
        version.OriginalValue = version.CurrentValue;
        return null;
    }

    internal static ProblemHttpResult Stale() => TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed,
        title: "This resource changed since you read it. Reload it and apply your change again.");

    /// <summary>Saves; a concurrent change answers 412 instead of throwing.</summary>
    internal static async Task<ProblemHttpResult?> SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }
    }
}
