using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tropicast.Dashboard.Infrastructure.Persistence;
using Tropicast.Dashboard.Infrastructure.Provisioning;

namespace Tropicast.Dashboard.Api.Operator;

/// <summary>People who run Tropicast itself (<c>Operators</c> section).</summary>
internal sealed class OperatorOptions
{
    /// <summary>Email addresses of operator accounts (confirmed accounts sign in as usual).</summary>
    public List<string> Emails { get; set; } = [];
}

internal sealed record OperatorRequirement : IAuthorizationRequirement;

internal sealed class OperatorHandler(IOptionsMonitor<OperatorOptions> options) : AuthorizationHandler<OperatorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperatorRequirement requirement)
    {
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        if (email is not null && options.CurrentValue.Emails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>A streaming node's provisioning state.</summary>
/// <param name="Node">Node name.</param>
/// <param name="InSync">The node runs the desired station limits.</param>
/// <param name="DesiredVersion">Version of the limits the database asks for.</param>
/// <param name="AppliedVersion">Version last applied to the node.</param>
/// <param name="AppliedAt">When it was applied.</param>
/// <param name="LastAttemptAt">Last apply attempt.</param>
/// <param name="FailedAttempts">Failures since the last success.</param>
/// <param name="NextAttemptAt">Next retry after a failure.</param>
/// <param name="LastError">Last failure.</param>
internal sealed record StreamingNodeResponse(string Node, bool InSync, string? DesiredVersion, string? AppliedVersion,
    DateTimeOffset? AppliedAt, DateTimeOffset? LastAttemptAt, int FailedAttempts, DateTimeOffset? NextAttemptAt, string? LastError);

internal static class OperatorEndpoints
{
    internal const string Policy = "operator";

    internal static void MapOperator(this IEndpointRouteBuilder app)
    {
        var nodes = app.MapGroup("/api/v1/operator/streaming-nodes").WithTags("Operator").RequireAuthorization(Policy);
        nodes.MapGet("", ListAsync).WithSummary("Station limits applied to each streaming node, with failures.");
        nodes.MapPost("/{node}/reconcile", ReconcileAsync).WithSummary("Retries applying station limits now.");
    }

    private static async Task<Ok<List<StreamingNodeResponse>>> ListAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var nodes = await db.StreamingNodes.OrderBy(n => n.Node).ToListAsync(cancellationToken);
        return TypedResults.Ok(nodes.Select(n => new StreamingNodeResponse(n.Node, n.InSync, n.DesiredVersion, n.AppliedVersion,
            n.AppliedAt, n.LastAttemptAt, n.FailedAttempts, n.NextAttemptAt, n.LastError)).ToList());
    }

    private static async Task<Results<Accepted, NotFound>> ReconcileAsync(string node, AppDbContext db, ProvisioningSignal signal,
        CancellationToken cancellationToken)
    {
        var state = await db.StreamingNodes.SingleOrDefaultAsync(n => n.Node == node, cancellationToken);
        if (state is null)
        {
            return TypedResults.NotFound();
        }
        await db.StreamingNodes.Where(n => n.Node == node)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.NextAttemptAt, (DateTimeOffset?)null), cancellationToken);
        signal.Wake();
        return TypedResults.Accepted((string?)null);
    }
}
