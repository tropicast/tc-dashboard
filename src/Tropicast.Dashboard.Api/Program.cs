using System.Text.Json.Serialization;
using Tropicast.Dashboard.Api;
using Scalar.AspNetCore;
using Tropicast.Dashboard.Api.Auth;
using Tropicast.Dashboard.Api.Stations;
using Tropicast.Dashboard.Api.Tenants;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
// Malformed request bodies are the client's fault: 400, not 500.
builder.Services.AddExceptionHandler(options =>
    options.StatusCodeSelector = exception => exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError);
// Enums travel by name ("Owner"), as they are stored; numbers are rejected, so undefined values never get in.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi(options => options.AddExamples().AddConcurrencyHeaders());
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDashboardAuth(builder.Configuration);
builder.Services.Configure<StreamingOptions>(builder.Configuration.GetSection("Streaming"));

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
app.MapHealthChecks("/health");
app.MapApi();
app.MapAuth();
app.MapInvitations();
app.MapTenants();
app.MapMembers();
app.MapStations();

// The React SPA is built into wwwroot; client-side routes fall back to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
