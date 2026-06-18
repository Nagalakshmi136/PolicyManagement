using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using PolicyManagement.Api.Auth;
using PolicyManagement.Api.Middleware;
using PolicyManagement.Api.Options;
using PolicyManagement.Application;
using PolicyManagement.Application.Common.Options;
using PolicyManagement.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// ---------------------------------------------------------------------------
// Bootstrap logger — captures startup failures before the host is built.
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // -----------------------------------------------------------------------
    // Serilog — read full config from appsettings.json
    // -----------------------------------------------------------------------
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    // -----------------------------------------------------------------------
    // Options — fail fast on missing / invalid config at startup
    // -----------------------------------------------------------------------
    builder.Services
        .AddOptions<KeycloakOptions>()
        .BindConfiguration(KeycloakOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));

    // -----------------------------------------------------------------------
    // Authentication — Keycloak JWT Bearer (RS256)
    // -----------------------------------------------------------------------
    var keycloak = builder.Configuration
        .GetSection(KeycloakOptions.SectionName)
        .Get<KeycloakOptions>() ?? new KeycloakOptions();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority             = keycloak.Authority;
            options.Audience              = keycloak.Audience;
            options.RequireHttpsMetadata  = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true
                // DO NOT set RoleClaimType — KeycloakRolesClaimsTransformation
                // maps realm_access.roles → ClaimTypes.Role after token validation.
            };
        });

    // Keycloak roles live under realm_access.roles, not a flat "roles" claim.
    // This transformer maps them to ClaimTypes.Role so named policies work.
    builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();

    // -----------------------------------------------------------------------
    // Authorization — named policies (never use [Authorize(Roles = "...")])
    // -----------------------------------------------------------------------
    builder.Services.AddAuthorization(options =>
    {
        // Require an authenticated user on all endpoints unless explicitly overridden.
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        options.AddPolicy("PolicyRead", policy =>
            policy.RequireRole("policy-reader", "policy-admin"));

        options.AddPolicy("PolicyWrite", policy =>
            policy.RequireRole("policy-admin"));
    });

    // -----------------------------------------------------------------------
    // MVC, API Versioning, OpenAPI
    // -----------------------------------------------------------------------
    builder.Services.AddControllers();

    builder.Services
        .AddApiVersioning(options =>
        {
            options.DefaultApiVersion                = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions               = true;
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat           = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

    builder.Services.AddOpenApi();

    // -----------------------------------------------------------------------
    // Infrastructure and Application layers
    // -----------------------------------------------------------------------
    builder.Services
        .AddHealthChecks()
        .AddInfrastructureHealthChecks();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // -----------------------------------------------------------------------
    // Build and configure the middleware pipeline
    // -----------------------------------------------------------------------
    var app = builder.Build();

    // Apply migrations and seed data in Development / Integration environments.
    // Program.cs references no Infrastructure concrete types — delegated to InfrastructureDI.
    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Integration"))
    {
        await PolicyManagement.Infrastructure.DependencyInjection.InitialiseDatabaseAsync(app.Services);
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // Middleware pipeline — order is mandated by architecture and auth standards.
    // 1. ExceptionHandlingMiddleware — outermost; converts all exceptions to ProblemDetails
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 2. CorrelationIdMiddleware — attaches X-Correlation-Id to request/response and log context
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 3. Serilog request logging — structured HTTP access logs
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost",   httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent",     httpContext.Request.Headers.UserAgent.ToString());
        };
    });

    // 4. Authentication — validates JWT, populates ClaimsPrincipal
    app.UseAuthentication();

    // 5. Authorization — evaluates [Authorize(Policy = "...")] — MUST follow UseAuthentication
    app.UseAuthorization();

    // 6. Controllers
    app.MapControllers();

    // Health probes — anonymous; .AllowAnonymous() overrides the fallback policy
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false  // liveness: process alive check only, no DB
    }).AllowAnonymous();

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    }).AllowAnonymous();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Expose Program to WebApplicationFactory in integration tests
public partial class Program { }
