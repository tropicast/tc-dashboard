using Tropicast.Dashboard.Api;
using Tropicast.Dashboard.Application;
using Tropicast.Dashboard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
}
app.MapHealthChecks("/health");
app.MapApi();

// The React SPA is built into wwwroot; client-side routes fall back to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
