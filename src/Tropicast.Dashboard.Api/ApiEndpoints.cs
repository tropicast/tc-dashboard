using System.Reflection;

namespace Tropicast.Dashboard.Api;

internal static class ApiEndpoints
{
    /// <summary>Maps the REST API under <c>/api/v1</c>. Unknown API paths answer 404 Problem Details, never the SPA.</summary>
    internal static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/version", () => TypedResults.Ok(new VersionInfo(AppVersion)))
            .WithName("GetVersion")
            .WithSummary("Version of the running API.");
        app.Map("/api/{**path}", () => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound))
            .ExcludeFromDescription();
    }

    private static string AppVersion { get; } =
        typeof(ApiEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}

/// <summary>API version information.</summary>
/// <param name="Version">Semantic version of the API build.</param>
internal sealed record VersionInfo(string Version);
