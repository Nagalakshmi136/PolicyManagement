using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PolicyManagement.Application;
using PolicyManagement.Infrastructure;
using PolicyManagement.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services
    .AddHealthChecks()
    .AddInfrastructureHealthChecks();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Apply migrations and seed data in Development / Integration environments.
// Program.cs references no Infrastructure concrete types — delegated to InfrastructureDI.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Integration"))
{
    await PolicyManagement.Infrastructure.DependencyInjection.InitialiseDatabaseAsync(app.Services);
}

// Middleware pipeline — order is mandated by architecture standards.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness: always healthy if the process is running.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false  // exclude all checks — liveness needs no DB
}).AllowAnonymous();

// Readiness: healthy only when the database is reachable.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();

// Expose Program to WebApplicationFactory in integration tests
public partial class Program { }
