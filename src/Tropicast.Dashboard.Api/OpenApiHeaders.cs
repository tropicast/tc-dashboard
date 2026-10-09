using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Tropicast.Dashboard.Api;

/// <summary>Endpoint metadata: the endpoint reads <c>If-Match</c>.</summary>
internal sealed record IfMatchMetadata(bool Required);

/// <summary>Endpoint metadata: successful responses carry an <c>ETag</c> (and <c>Location</c> on 201).</summary>
internal sealed record ETagMetadata;

/// <summary>Documents the concurrency headers in OpenAPI, so generated clients can send and read them.</summary>
internal static class OpenApiHeaders
{
    internal static RouteHandlerBuilder WithIfMatch(this RouteHandlerBuilder builder, bool required = true)
        => builder.WithMetadata(new IfMatchMetadata(required));

    internal static RouteHandlerBuilder WithETag(this RouteHandlerBuilder builder) => builder.WithMetadata(new ETagMetadata());

    internal static OpenApiOptions AddConcurrencyHeaders(this OpenApiOptions options) => options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IfMatchMetadata>().FirstOrDefault() is { } ifMatch)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match",
                In = ParameterLocation.Header,
                Required = ifMatch.Required,
                Description = "The ETag from your last read. Missing: 428; stale: 412.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }
        if (metadata.OfType<ETagMetadata>().Any() && operation.Responses is { } responses)
        {
            foreach (var (status, response) in responses.Where(r => r.Key is "200" or "201"))
            {
                if (response is not OpenApiResponse concrete)
                {
                    continue;
                }
                concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
                concrete.Headers["ETag"] = new OpenApiHeader
                {
                    Description = "Version of the resource; send it back as If-Match to change it.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                };
                if (status == "201")
                {
                    concrete.Headers["Location"] = new OpenApiHeader
                    {
                        Description = "Address of the created resource.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" },
                    };
                }
            }
        }
        return Task.CompletedTask;
    });
}
