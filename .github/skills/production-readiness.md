---
applyTo: "src/**/*.cs,src/**/*.json,src/**/*.yaml,src/**/*.yml,**/Dockerfile,**/docker-compose*.yml"
---

# Production Readiness Standards — PolicyManagement BFF

## Overview

Every feature merged to `main` must meet these standards. Production readiness is not a final phase — it is a continuous constraint applied from the first commit.

---

## Structured Logging with Serilog

### Package Setup

```xml
<!-- PolicyManagement.Api.csproj -->
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
<PackageReference Include="Serilog.Sinks.File" Version="5.*" />
<PackageReference Include="Serilog.Enrichers.Environment" Version="2.*" />
<PackageReference Include="Serilog.Enrichers.Thread" Version="3.*" />
<PackageReference Include="Serilog.Enrichers.Process" Version="2.*" />
```

### Bootstrap Configuration

```csharp
// Program.cs — configure Serilog before the host is built
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .WriteTo.Console(new JsonFormatter())  // structured JSON to stdout
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName());

    // ... register services

    var app = builder.Build();
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        };
    });

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
```

### appsettings.json — Serilog Section

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog" }
      }
    ],
    "Enrich": ["FromLogContext", "WithThreadId", "WithEnvironmentName"],
    "Properties": {
      "Application": "PolicyManagement.Api"
    }
  }
}
```

### Logging in Application Code

Use `ILogger<T>` injected via DI — never `Log.ForContext<T>()` (static) in production code.

```csharp
public class CreatePolicyCommandHandler(
    IPolicyRepository policyRepository,
    ILogger<CreatePolicyCommandHandler> logger)
{
    public async Task<PolicyDto> Handle(CreatePolicyCommand command, CancellationToken ct)
    {
        logger.LogInformation("Creating policy for customer {CustomerId} with product {ProductCode}",
            command.CustomerId, command.ProductCode);

        // ... create policy

        logger.LogInformation("Policy {PolicyId} created successfully for customer {CustomerId}",
            policy.Id, command.CustomerId);

        return policy.ToDto();
    }
}
```

### Logging Rules

| Rule | Detail |
|---|---|
| **Use structured properties** | `"Policy {PolicyId} created"` not `$"Policy {policyId} created"` — enables log querying |
| **Never log sensitive data** | No PII, passwords, card numbers, full policy documents in log messages |
| **Log levels** | `Debug`: developer diagnostics; `Information`: business events; `Warning`: recoverable anomalies; `Error`: exceptions; `Fatal`: host failure |
| **Correlation ID** | Every inbound request must carry a `X-Correlation-Id` header; enrich all log entries with it |
| **Avoid `LogError` for expected domain errors** | `NotFoundException` and `ValidationException` are `Warning`; only unexpected faults are `Error` |
| **No `Console.WriteLine`** | All output goes through Serilog — never `Console.Write*` or `Debug.WriteLine` in production code |

### Correlation ID Middleware

```csharp
// Api/Middleware/CorrelationIdMiddleware.cs
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
```

Register before `UseSerilogRequestLogging` in `Program.cs`.

---

## Health Check Endpoint Requirements

### Package Setup

```xml
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="9.*" />
<PackageReference Include="AspNetCore.HealthChecks.SqlServer" Version="8.*" />
```

### Registration

```csharp
// DependencyInjection or Program.cs
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(
        name: "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"])
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sql-server",
        failureStatus: HealthStatus.Degraded,
        tags: ["ready"])
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
```

### Endpoint Mapping

```csharp
// Program.cs
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var response = new
    {
        status = report.Status.ToString(),
        duration = report.TotalDuration,
        entries = report.Entries.ToDictionary(
            e => e.Key,
            e => new { status = e.Value.Status.ToString(), description = e.Value.Description, duration = e.Value.Duration })
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(response));
}
```

### Required Endpoints

| Endpoint | Tags | Purpose | Expected consumers |
|---|---|---|---|
| `GET /health/live` | `live` | Is the process alive? | Kubernetes `livenessProbe` |
| `GET /health/ready` | `ready` | Can the process serve traffic? | Kubernetes `readinessProbe` |

**Rules:**
- Health endpoints must **not** require authentication
- `/health/ready` must fail (`Unhealthy`) if the database is unreachable
- `/health/live` must return `200 Healthy` even when the database is down (process is alive but not ready)
- Health endpoints must not be included in Swagger docs or API versioning

---

## Error Handling and ProblemDetails Middleware

### Global Exception Handling Middleware

```csharp
// Api/Middleware/ExceptionHandlingMiddleware.cs
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail) = exception switch
        {
            NotFoundException notFound       => (404, "Not Found",               notFound.Message),
            DomainException domain           => (422, "Unprocessable Entity",    domain.Message),
            ValidationException validation   => (400, "Bad Request",             "One or more validation errors occurred."),
            UnauthorizedAccessException _    => (403, "Forbidden",               "You do not have permission to perform this action."),
            _                                => (500, "Internal Server Error",   "An unexpected error occurred.")
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type   = $"https://tools.ietf.org/html/rfc9110#section-15.5.{statusCode - 399}",
            Title  = title,
            Status = statusCode,
            Detail = statusCode == 500 ? null : detail,   // never leak internal detail on 500
            Instance = context.Request.Path
        };

        if (exception is ValidationException validationEx)
        {
            var validationProblem = new ValidationProblemDetails(
                validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

            validationProblem.Type     = problem.Type;
            validationProblem.Instance = problem.Instance;
            return context.Response.WriteAsJsonAsync(validationProblem);
        }

        return context.Response.WriteAsJsonAsync(problem);
    }
}
```

### Registration Order in Program.cs

```csharp
// Middleware order matters — exception handler must be outermost
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

### ProblemDetails Rules

- `500` responses must **never** include `detail`, stack traces, connection strings, or internal type names
- `400` responses must include the `errors` map (`ValidationProblemDetails`)
- `422` responses carry the domain rule message in `detail`; the `errors` map is omitted
- All error responses use `Content-Type: application/problem+json`
- The `instance` field must be the request path (e.g., `/api/v1/policies/POL-001`)
- Log level for exceptions: `Warning` for `404`/`422`/`400`; `Error` for `500`

---

## Configuration Management (12-Factor)

### Principle: Config from Environment

Configuration must never be hard-coded. All environment-specific values come from environment variables, which override `appsettings.json`.

### Configuration Hierarchy (highest → lowest priority)

```
1. Environment variables           (production, CI)
2. appsettings.{Environment}.json  (overrides per environment)
3. appsettings.json                (defaults and non-sensitive structure)
4. User Secrets                    (local development only, never committed)
```

### Required Configuration Sections

```json
// appsettings.json — structure only, no secrets
{
  "ConnectionStrings": {
    "DefaultConnection": ""           // set via env: ConnectionStrings__DefaultConnection
  },
  "Authentication": {
    "Authority": "",                   // set via env: Authentication__Authority
    "Audience": ""                     // set via env: Authentication__Audience
  },
  "Cache": {
    "SlidingExpirationSeconds": 300,
    "AbsoluteExpirationSeconds": 3600
  },
  "Serilog": { }
}
```

### Strongly-Typed Options

Use the Options pattern — never read `IConfiguration` directly in application or domain code.

```csharp
// Defined in Application or Api layer
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    [Required, Url] public string Authority { get; init; } = string.Empty;
    [Required]      public string Audience  { get; init; } = string.Empty;
}

// Registration
builder.Services
    .AddOptions<AuthenticationOptions>()
    .BindConfiguration(AuthenticationOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();   // fail fast at startup if config is invalid
```

### Environment Variable Naming

ASP.NET Core maps `__` (double underscore) to `:` in configuration keys:

| appsettings.json path | Environment variable |
|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Authentication:Authority` | `Authentication__Authority` |
| `Cache:SlidingExpirationSeconds` | `Cache__SlidingExpirationSeconds` |

### Secrets Management

| Environment | Secret storage |
|---|---|
| Local development | `dotnet user-secrets` — never `appsettings.json` |
| CI pipeline | CI/CD secret store (GitHub Actions Secrets, Azure Key Vault reference) |
| Production | Azure Key Vault, AWS Secrets Manager, or orchestrator secrets |

**Rules:**
- `appsettings.*.json` files must **never** contain passwords, connection strings with credentials, API keys, or certificates
- `.gitignore` must include `appsettings.*.local.json` and `secrets.json`
- `ValidateOnStart()` must be called on all options registrations so misconfiguration fails at startup, not at first use
- Never inject `IConfiguration` beyond `Program.cs` / DI registration code

---

## Docker and Docker Compose Requirements

### Dockerfile

```dockerfile
# src/PolicyManagement.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files first (layer caching)
COPY ["src/PolicyManagement.Api/PolicyManagement.Api.csproj",            "src/PolicyManagement.Api/"]
COPY ["src/PolicyManagement.Application/PolicyManagement.Application.csproj", "src/PolicyManagement.Application/"]
COPY ["src/PolicyManagement.Domain/PolicyManagement.Domain.csproj",      "src/PolicyManagement.Domain/"]
COPY ["src/PolicyManagement.Infrastructure/PolicyManagement.Infrastructure.csproj", "src/PolicyManagement.Infrastructure/"]

RUN dotnet restore "src/PolicyManagement.Api/PolicyManagement.Api.csproj"

COPY . .
WORKDIR "/src/src/PolicyManagement.Api"
RUN dotnet build "PolicyManagement.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PolicyManagement.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Run as non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PolicyManagement.Api.dll"]
```

### Dockerfile Rules

- Use the official Microsoft .NET 10 images — no custom base images
- Multi-stage build is mandatory (build stage never ships to production)
- Copy `.csproj` files and `dotnet restore` before `COPY . .` to maximise layer cache hits
- Run as a **non-root user** (`appuser`) in the final stage
- `EXPOSE` both HTTP (`8080`) and HTTPS (`8081`) ports
- Never copy `appsettings.*.local.json`, `user-secrets`, or `*.Development.json` into the image — use `.dockerignore`

### .dockerignore

```
**/.git
**/.vs
**/bin
**/obj
**/*.user
**/appsettings.Development.json
**/appsettings.*.local.json
**/user-secrets.json
**/Dockerfile*
**/.dockerignore
**/docker-compose*
README.md
docs/
tests/
```

### docker-compose.yml

```yaml
# docker-compose.yml — local development orchestration
services:
  api:
    build:
      context: .
      dockerfile: src/PolicyManagement.Api/Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Server=db;Database=PolicyManagement;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True;
      - Keycloak__Authority=http://keycloak:8080/realms/policy-mgmt
      - Keycloak__Audience=policy-management-api
    depends_on:
      db:
        condition: service_healthy
      keycloak:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 15s

  keycloak:
    image: quay.io/keycloak/keycloak:24.0
    command: start-dev --import-realm
    ports:
      - "8180:8080"
    environment:
      - KEYCLOAK_ADMIN=${KEYCLOAK_ADMIN}
      - KEYCLOAK_ADMIN_PASSWORD=${KEYCLOAK_ADMIN_PASSWORD}
    volumes:
      - ./keycloak/realm-export.json:/opt/keycloak/data/import/realm-export.json
    healthcheck:
      test: ["CMD-SHELL", "curl -sf http://localhost:8080/health/ready"]
      interval: 10s
      timeout: 5s
      retries: 15
      start_period: 30s

  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    ports:
      - "1433:1433"
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=${SA_PASSWORD}
      - MSSQL_PID=Developer
    volumes:
      - sqldata:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P ${SA_PASSWORD} -Q 'SELECT 1'"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 20s

volumes:
  sqldata:
```

### .env.example (committed; .env is gitignored)

```env
# .env.example — copy to .env and fill in values for local development
SA_PASSWORD=YourStrong@Password123
KEYCLOAK_ADMIN=admin
KEYCLOAK_ADMIN_PASSWORD=YourAdminPassword123
```

### Docker Compose Rules

- Secrets are never hard-coded in `docker-compose.yml` — use environment variables from `.env`
- `.env` is in `.gitignore`; `.env.example` is committed with placeholder values
- `depends_on` with `condition: service_healthy` ensures the API only starts after the database is ready
- The `api` service healthcheck must call `/health/live` — not `/health/ready` (avoids circular dependency on DB healthcheck)
- Named volumes are used for database persistence — never anonymous volumes

---

## Caching Strategy

### Cache Layers

| Layer | Tool | Use case |
|---|---|---|
| In-process | `IMemoryCache` | Single-instance, low-latency, short-lived reference data |
| Distributed | `IDistributedCache` (Redis or SQL Server) | Multi-instance deployments, session state, longer-lived cache |

Default to **`IMemoryCache`** for the initial implementation. Switch to distributed cache when horizontal scaling is required.

### Registration

```csharp
// DependencyInjection.cs (Application or Infrastructure)
services.AddMemoryCache();

// For distributed cache (future):
// services.AddStackExchangeRedisCache(options =>
// {
//     options.Configuration = configuration.GetConnectionString("Redis");
//     options.InstanceName = "PolicyManagement:";
// });
```

### Cache Key Conventions

```csharp
public static class CacheKeys
{
    public static string Policy(string policyId)     => $"policy:{policyId}";
    public static string CustomerPolicies(string customerId) => $"customer:{customerId}:policies";
    public static string ProductCodes()              => "reference:product-codes";
}
```

**Rules:**
- All cache keys must be defined in a single `CacheKeys` static class — no inline strings
- Keys follow `noun:id[:sub-resource]` format (lowercase, colon-separated)
- Cache keys must include a version prefix when a schema change would invalidate all entries: `"v2:policy:{policyId}"`

### Cache Expiration

```csharp
// Read from configuration — never hard-code durations in handlers
public sealed class CacheOptions
{
    public const string SectionName = "Cache";
    public int SlidingExpirationSeconds  { get; init; } = 300;   // 5 min default
    public int AbsoluteExpirationSeconds { get; init; } = 3600;  // 1 hr default
}
```

| Data type | Recommended expiry | Notes |
|---|---|---|
| Individual policy (read) | 5 min sliding | Invalidate on write |
| Reference/lookup data (product codes, statuses) | 1 hr absolute | Rarely changes |
| Paginated list queries | 1 min absolute | High churn, short TTL |
| User-scoped data | Never cache | Per-user data must not be shared across cache entries |

### Cache-Aside Pattern

```csharp
public async Task<PolicyDto?> Handle(GetPolicyByIdQuery query, CancellationToken ct)
{
    var cacheKey = CacheKeys.Policy(query.PolicyId);

    if (_cache.TryGetValue(cacheKey, out PolicyDto? cached))
        return cached;

    var policy = await _policyRepository.GetByIdAsync(query.PolicyId, ct);
    if (policy is null) return null;

    var dto = policy.ToDto();
    _cache.Set(cacheKey, dto, new MemoryCacheEntryOptions
    {
        SlidingExpiration  = TimeSpan.FromSeconds(_cacheOptions.SlidingExpirationSeconds),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_cacheOptions.AbsoluteExpirationSeconds)
    });

    return dto;
}
```

### Cache Invalidation on Write

```csharp
public async Task<PolicyDto> Handle(UpdatePolicyCommand command, CancellationToken ct)
{
    // ... update policy in repository

    _cache.Remove(CacheKeys.Policy(command.PolicyId));
    _cache.Remove(CacheKeys.CustomerPolicies(policy.CustomerId));

    return policy.ToDto();
}
```

### Caching Rules

- **Never cache write operations** — only cache reads
- **Invalidate eagerly on mutation** — remove all related cache entries when a resource is created, updated, or deleted
- **Never cache user-scoped or sensitive data** in a shared cache entry
- **Cache in the Application layer** (`IMemoryCache` / `IDistributedCache`) — never in controllers or domain entities
- **Cache DTOs, not domain entities** — serialised domain entities may carry behaviour or lazy-load state that is unsafe to cache

---

## Production Readiness Checklist

Before promoting any feature to `main`:

### Logging
- [ ] All significant business events logged at `Information` with structured properties
- [ ] No sensitive data (PII, credentials) in any log statement
- [ ] `CorrelationIdMiddleware` registered and enriching log context
- [ ] No `Console.WriteLine` or static `Log.*` calls in production code

### Health Checks
- [ ] `/health/live` and `/health/ready` endpoints respond correctly
- [ ] `/health/ready` returns `Unhealthy` when the database is down
- [ ] Health endpoints excluded from auth and Swagger

### Error Handling
- [ ] All unhandled exceptions produce `ProblemDetails` with correct status codes
- [ ] `500` responses contain no `detail`, stack trace, or internal type names
- [ ] `400` responses include the `errors` field map
- [ ] Middleware registration order is correct (exception handler is outermost)

### Configuration
- [ ] No secrets in `appsettings*.json` or source control
- [ ] All options classes registered with `ValidateOnStart()`
- [ ] `.env.example` committed; `.env` gitignored
- [ ] App fails fast at startup when required configuration is missing

### Docker
- [ ] `Dockerfile` uses multi-stage build and runs as non-root user
- [ ] `.dockerignore` excludes `bin/`, `obj/`, dev config files, and test projects
- [ ] `docker-compose.yml` uses `depends_on` with `condition: service_healthy`
- [ ] No hard-coded secrets in `docker-compose.yml`

### Caching
- [ ] All cache keys defined in `CacheKeys` static class
- [ ] Cache expiration driven by `CacheOptions` configuration, not hard-coded durations
- [ ] Write operations invalidate all related cache entries
- [ ] No user-scoped or sensitive data cached in shared entries
