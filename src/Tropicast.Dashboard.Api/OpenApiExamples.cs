using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Api.Tenants;
using Tropicast.Dashboard.Application.Stations;
using Tropicast.Dashboard.Application.Tenants;

namespace Tropicast.Dashboard.Api;

/// <summary>Example bodies in the OpenAPI document (shown by Scalar in development).</summary>
internal static class OpenApiExamples
{
    private static readonly Dictionary<Type, string> Examples = new()
    {
        [typeof(CreateTenantCommand)] = """{"name":"Radio Mada Group","slug":"radio-mada"}""",
        [typeof(UpdateTenantCommand)] = """{"name":"Radio Mada Media"}""",
        [typeof(CreateStationCommand)] = """
            {"name":"Radio Mada","slug":"radio-mada","description":"News and salegy from Antananarivo","genre":"Talk",
             "country":"MG","language":"mg","website":"https://radiomada.example","listInDirectory":true}
            """,
        [typeof(UpdateStationCommand)] = """{"genre":"Salegy","description":"Music all night"}""",
        [typeof(CreateCredentialCommand)] = """{"deviceLabel":"Studio PC"}""",
        [typeof(InviteRequest)] = """{"email":"dj@example.com","role":"Broadcaster"}""",
        [typeof(StationResponse)] = """
            {"id":"01927f5e-6c1a-7b3e-9a52-3f1d2c4b5a69","publicId":"k3m9x2p7qa","name":"Radio Mada","slug":"radio-mada",
             "description":"News and salegy from Antananarivo","genre":"Talk","country":"MG","language":"mg","logoUrl":null,
             "website":"https://radiomada.example","listInDirectory":true,"createdAt":"2026-10-09T12:00:00+00:00",
             "listenerUrls":{"mp3":"https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3",
                             "opus":"https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.opus"}}
            """,
    };

    internal static OpenApiOptions AddExamples(this OpenApiOptions options) => options.AddSchemaTransformer((schema, context, _) =>
    {
        if (Examples.TryGetValue(context.JsonTypeInfo.Type, out var example))
        {
            schema.Examples = [JsonNode.Parse(example)!];
        }
        return Task.CompletedTask;
    });
}
