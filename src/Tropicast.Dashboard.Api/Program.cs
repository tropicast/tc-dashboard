using Tropicast.Dashboard.Api.Billing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Npgsql;
using Tropicast.Dashboard.Infrastructure.Telemetry;
using Tropicast.Dashboard.Api.Logging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Tropicast.Dashboard.Infrastructure.Persistence;
using System.Text.Json.Serialization;
using Tropicast.Dashboard.Api;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Operator;
using Tropicast.Dashboard.Api.Public;
using Tropicast.Dashboard.Api.SourceAuth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Api.Tenants;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Infrastructure;
using Tropicast.Dashboard.Infrastructure.Provisioning;
using Tropicast.Dashboard.Infrastructure.Stats;
using Tropicast.Dashboard.Infrastructure.Directories;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
{
    // JSON lines with credentials masked (Logging/Redaction.cs); development keeps the readable console.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.FormatterName = RedactingJsonFormatter.Name)
        .AddConsoleFormatter<RedactingJsonFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
}
builder.Services.AddProblemDetails();
// Malformed request bodies are the client's fault: 400, not 500.
builder.Services.AddExceptionHandler(options =>
    options.StatusCodeSelector = exception => exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError);
// Enums travel by name ("Owner"), as they are stored; numbers are rejected, so undefined values never get in.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi(options => options.AddExamples().AddConcurrencyHeaders());
// /health/live: the process answers (no dependencies). /health/ready: it can serve, i.e. the database answers.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// OpenTelemetry traces and metrics, exported over OTLP to the node's Alloy (then Grafana Cloud) when
// OTEL_EXPORTER_OTLP_ENDPOINT is set; otherwise collected but not sent.
var otlp = !string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("tc-dashboard", serviceVersion: ApiEndpoints.AppVersion))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation()
            .AddNpgsql();
        if (otlp)
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
            .AddMeter(DashboardMetrics.MeterName);
        if (otlp)
        {
            metrics.AddOtlpExporter();
        }
    });
builder.Services.AddDashboardAuth(builder.Configuration);
builder.Services.Configure<StreamingOptions>(builder.Configuration.GetSection("Streaming"));
builder.Services.Configure<SourceAuthOptions>(builder.Configuration.GetSection("SourceAuth"));
// One node setting: provisioning applies limits to the node stations are assigned to (Streaming:Node).
var streamingNode = builder.Configuration["Streaming:Node"] ?? new StreamingOptions().Node;
if (builder.Configuration["Provisioning:Node"] is { } provisioningNode && provisioningNode != streamingNode)
{
    throw new InvalidOperationException(
        $"Provisioning:Node ({provisioningNode}) differs from Streaming:Node ({streamingNode}). Set only Streaming:Node.");
}
builder.Services.PostConfigure<ProvisioningOptions>(options => options.Node = streamingNode);
builder.Services.PostConfigure<StatsOptions>(options => options.Node = streamingNode);
// Directory entries carry the public listener and page URLs: same settings as the rest of the API.
builder.Services.AddOptions<DirectoryOptions>().PostConfigure<IOptions<StreamingOptions>, IOptions<AppOptions>>((options, streaming, app) =>
{
    options.ListenerBaseUrl = streaming.Value.ListenerBaseUrl;
    options.PublicBaseUrl = new Uri(app.Value.PublicBaseUrl);
});
builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection("Operators"));
builder.Services.AddScoped<IAuthorizationHandler, OperatorHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(OperatorEndpoints.Policy, policy => policy.RequireAuthenticatedUser().AddRequirements(new OperatorRequirement()));

var app = builder.Build();
// Local development convenience (compose sets it); production runs the migration bundle at deploy.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API reference at /scalar, in development only.
    app.MapScalarApiReference();
}
app.UseDashboardAuth();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
// Kept for existing checks (image smoke test, uptime monitors): the same as /health/live.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapApi();
app.MapAuth();
app.MapAccount();
app.MapDeviceAuthorization();
app.MapInvitations();
app.MapTenants();
app.MapMembers();
app.MapAudit();
app.MapStations();
app.MapCredentials();
app.MapDesktop();
app.MapStationStats();
app.MapPublicStations();
app.MapDirectory();
app.MapBilling();
app.MapSourceAuth();
app.MapOperator();

// The React SPA is built into wwwroot; client-side routes fall back to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
