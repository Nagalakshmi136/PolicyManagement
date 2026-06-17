---
applyTo: "src/PolicyManagement.Api/**/*.cs,tests/PolicyManagement.Api.Tests/**/*.cs"
---

# Authentication and Authorization Standards — PolicyManagement BFF

## Overview

Authentication is handled by **Keycloak 24+** via **JWT Bearer (OAuth2/OIDC)**. The BFF validates every inbound JWT against the Keycloak realm and enforces access using **ASP.NET Core policy-based authorization**. The architectural decision is documented in `docs/adr/ADR-010-keycloak-jwt-bearer-authentication.md`. This skill defines how to implement it correctly.

**Roles:**
- `policy-reader` — read access to policy data (`GET` endpoints)
- `policy-admin` — full access including write operations (`PATCH` endpoints)

**Named policies (defined once in `Program.cs`):**
- `PolicyRead` — requires `policy-reader` OR `policy-admin`
- `PolicyWrite` — requires `policy-admin` only

---

## 1. JWT Bearer Configuration

### Package

```xml
<!-- PolicyManagement.Api.csproj -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.*" />
```

### KeycloakOptions (Options Pattern)

```csharp
// Api/Options/KeycloakOptions.cs
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    [Required, Url] public string Authority { get; init; } = string.Empty;
    [Required]      public string Audience  { get; init; } = string.Empty;
}
```

### appsettings.json — Structure Only (no secrets)

```json
{
  "Keycloak": {
    "Authority": "",
    "Audience":  ""
  }
}
```

Environment variable overrides:
| appsettings key | Environment variable |
|---|---|
| `Keycloak:Authority` | `Keycloak__Authority` |
| `Keycloak:Audience` | `Keycloak__Audience` |

### Registration in Program.cs

```csharp
// Program.cs
builder.Services
    .AddOptions<KeycloakOptions>()
    .BindConfiguration(KeycloakOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();   // fail fast — never discover missing config at first request

var keycloak = builder.Configuration
    .GetSection(KeycloakOptions.SectionName)
    .Get<KeycloakOptions>()!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloak.Authority;
        options.Audience  = keycloak.Audience;

        // Only allow HTTP metadata discovery in Development (Keycloak on localhost)
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            // DO NOT set RoleClaimType here.
            // Keycloak does not emit a flat "roles" claim.
            // KeycloakRolesClaimsTransformation maps realm_access.roles → ClaimTypes.Role.
        };
    });
```

**Rules:**
- `ValidateOnStart()` is mandatory — a missing `Keycloak__Authority` must crash the process at startup, not at the first authenticated request
- `RequireHttpsMetadata = false` only when `IsDevelopment()` — never hard-coded to `false` unconditionally
- Do **not** set `RoleClaimType` in `TokenValidationParameters` — role mapping is handled entirely by `KeycloakRolesClaimsTransformation` (see §2)
- Do **not** set `NameClaimType` — use the default `sub` claim

---

## 2. Keycloak Roles Claims Transformation

### Why It Is Needed

Keycloak does not emit roles as a flat JWT claim. Instead, realm-level roles are nested under `realm_access`:

```json
{
  "realm_access": {
    "roles": ["policy-reader", "offline_access", "uma_authorization"]
  }
}
```

ASP.NET Core's `[Authorize(Policy = "...")]` evaluates roles via `ClaimTypes.Role`. Without transformation the role claims are never populated and all policy checks silently fail with `403`.

### Implementation

```csharp
// Api/Auth/KeycloakRolesClaimsTransformation.cs
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    private const string RealmAccessClaimType = "realm_access";
    private const string RolesKey             = "roles";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Avoid duplicate transformation on the same principal
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role))
            return Task.FromResult(principal);

        var realmAccessClaim = principal.FindFirst(RealmAccessClaimType);
        if (realmAccessClaim is null)
            return Task.FromResult(principal);

        using var realmAccess = JsonDocument.Parse(realmAccessClaim.Value);
        if (!realmAccess.RootElement.TryGetProperty(RolesKey, out var rolesElement))
            return Task.FromResult(principal);

        var identity = new ClaimsIdentity();

        foreach (var role in rolesElement.EnumerateArray())
        {
            var roleName = role.GetString();
            if (!string.IsNullOrWhiteSpace(roleName))
                identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
        }

        principal.AddIdentity(identity);
        return Task.FromResult(principal);
    }
}
```

### Registration

```csharp
// Program.cs — register after AddAuthentication/AddJwtBearer
builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();
```

**Rules:**
- Registered as `Transient` — `IClaimsTransformation` is called per-request by ASP.NET Core
- The idempotency guard (`HasClaim(c => c.Type == ClaimTypes.Role)`) prevents double-adding roles if the pipeline calls the transformer more than once
- This transformer must only add `ClaimTypes.Role` claims — it must not mutate any other claims on the principal

---

## 3. Policy-Based Authorization

### Golden Rule

> **Never** use `[Authorize(Roles = "...")]` on controller actions.  
> **Always** use named policies defined once in `Program.cs` and applied via `[Authorize(Policy = "...")]`.

This keeps role names out of controller code. Adding a new role to an existing policy requires changing one line in `Program.cs` — zero controller changes.

### Policy Definitions

```csharp
// Program.cs
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PolicyRead", policy =>
        policy.RequireRole("policy-reader", "policy-admin"));

    options.AddPolicy("PolicyWrite", policy =>
        policy.RequireRole("policy-admin"));
});
```

### Applying Policies on Controllers

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/policies")]
public class PoliciesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> List([FromQuery] ListPoliciesRequest request, CancellationToken ct) { ... }

    [HttpGet("{id}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct) { ... }

    [HttpGet("summary")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> Summary(CancellationToken ct) { ... }

    [HttpPatch("flag")]
    [Authorize(Policy = "PolicyWrite")]
    public async Task<IActionResult> BulkFlag([FromBody] BulkFlagRequest request, CancellationToken ct) { ... }
}
```

### Adding New Policies

To add a new role or policy, edit only `Program.cs`:

```csharp
// Example: adding a future "policy-auditor" role to PolicyRead
options.AddPolicy("PolicyRead", policy =>
    policy.RequireRole("policy-reader", "policy-admin", "policy-auditor"));
```

No controller files need to change.

---

## 4. Middleware Registration Order

The exact order in `Program.cs` is mandatory. Authentication must run before authorization; exception handling must be outermost.

```csharp
// Program.cs — middleware pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();  // 1 — outermost; catches all unhandled exceptions
app.UseMiddleware<CorrelationIdMiddleware>();       // 2 — enriches log context with X-Correlation-Id
app.UseSerilogRequestLogging();                    // 3 — structured HTTP request logs
app.UseAuthentication();                           // 4 — validates JWT, populates ClaimsPrincipal
app.UseAuthorization();                            // 5 — evaluates [Authorize(Policy = "...")] — MUST follow UseAuthentication
app.MapControllers();                              // 6 — route to controller actions
```

### Why Order Matters

| Swap | Consequence |
|---|---|
| `UseAuthorization` before `UseAuthentication` | Principal is always unauthenticated → all policy checks return `403` |
| `ExceptionHandlingMiddleware` not outermost | Auth errors (e.g., malformed JWT) escape as unformatted 500 responses |
| `MapControllers` before `UseAuthorization` | Authorization middleware never runs; all endpoints effectively unprotected |

---

## 5. Anonymous Endpoints

Only the two health check endpoints are anonymous. All `/api/v1/**` endpoints must be protected.

```csharp
// Program.cs
app.MapHealthChecks("/health/live",  new HealthCheckOptions { ... }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ... }).AllowAnonymous();
```

**Rules:**
- Use `.AllowAnonymous()` on the `MapHealthChecks` call — not `[AllowAnonymous]` on a controller
- Never apply `[AllowAnonymous]` to any controller or action under `/api/v1/`
- Never add a fallback anonymous policy globally to `AddAuthorization` — all undecorated endpoints should require authentication by default:
  ```csharp
  // Program.cs
  builder.Services.AddAuthorization(options =>
  {
      // Require authenticated user on all endpoints unless explicitly overridden
      options.FallbackPolicy = new AuthorizationPolicyBuilder()
          .RequireAuthenticatedUser()
          .Build();
      // Named policies defined below override the fallback
      options.AddPolicy("PolicyRead", ...);
      options.AddPolicy("PolicyWrite", ...);
  });
  ```

---

## 6. Integration Test Auth Bypass

Keycloak must **not** be started during CI or test runs. Tests use a `TestAuthHandler` that issues a synthetic `ClaimsPrincipal` without any real token validation.

### TestAuthHandler

```csharp
// tests/PolicyManagement.Api.Tests/Common/TestAuthHandler.cs
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";

    // Injected per-test via IHttpContextAccessor or request header
    public static readonly string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Read role from request header so individual tests can override
        var role = Context.Request.Headers[RoleHeader].FirstOrDefault() ?? "policy-admin";

        var claims = new[]
        {
            new Claim(ClaimTypes.Name,           "test-user"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Role,            role),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

### Registration in ApiWebApplicationFactory

```csharp
// tests/PolicyManagement.Api.Tests/Common/ApiWebApplicationFactory.cs
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureTestServices(services =>
    {
        // Remove real JWT Bearer and replace with TestAuth
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

        // Replace real DbContext with in-memory
        var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
        services.Remove(descriptor);
        services.AddDbContext<AppDbContext>(o =>
            o.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
    });
}
```

### Default Client (policy-admin)

```csharp
// In test class constructor — no X-Test-Role header = defaults to policy-admin
_adminClient = factory.CreateClient();
```

### Reader-Only Client (policy-reader)

```csharp
// Create a client that sends X-Test-Role: policy-reader on every request
_readerClient = factory.CreateClient();
_readerClient.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "policy-reader");
```

### Example — Assert 403 for PolicyWrite with Reader Role

```csharp
[Fact]
public async Task PATCH_Policies_Flag_WithReaderRole_Returns403()
{
    // Arrange
    var request = new BulkFlagRequest(new[] { SeedData.ExistingPolicyId });

    // Act — _readerClient sends X-Test-Role: policy-reader
    var response = await _readerClient.PatchAsJsonAsync("/api/v1/policies/flag", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

### Rules

- `TestAuthHandler` replaces `AddJwtBearer` entirely in tests — no real token is validated
- Keycloak must not appear in `docker-compose.override.yml` used by CI
- Never use `[AllowAnonymous]` to avoid adding `TestAuthHandler` in tests
- Every integration test class that tests a protected endpoint must use `ApiWebApplicationFactory` (which installs `TestAuthHandler`) — never bypass auth by setting `FallbackPolicy` to anonymous

---

## 7. Local Development Setup

### Keycloak in Docker Compose

```yaml
# docker-compose.yml
services:
  keycloak:
    image: quay.io/keycloak/keycloak:24.0
    command: start-dev --import-realm
    ports:
      - "8180:8080"
    environment:
      - KEYCLOAK_ADMIN=${KEYCLOAK_ADMIN_USER}
      - KEYCLOAK_ADMIN_PASSWORD=${KEYCLOAK_ADMIN_PASSWORD}
    volumes:
      - ./keycloak/realm-export.json:/opt/keycloak/data/import/realm-export.json:ro
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:8080/health/ready"]
      interval: 10s
      timeout: 5s
      retries: 15
      start_period: 30s
```

Keycloak auto-imports `keycloak/realm-export.json` on startup, which pre-creates:
- Realm: `policy-management`
- Client: `policy-management-api` (audience)
- Roles: `policy-reader`, `policy-admin`
- Seed users: one per role (credentials set via `.env` — never hard-coded)

### .env.example (committed; .env is gitignored)

```env
# Keycloak admin credentials
KEYCLOAK_ADMIN_USER=admin
KEYCLOAK_ADMIN_PASSWORD=YourAdminPassword

# Seed user credentials — one per role
READER_USER_PASSWORD=YourReaderPassword
ADMIN_USER_PASSWORD=YourAdminPassword2
```

### appsettings.Development.json

```json
{
  "Keycloak": {
    "Authority": "http://localhost:8180/realms/policy-management",
    "Audience":  "policy-management-api"
  }
}
```

### Obtaining a Test Token with curl

```bash
# Get a token for the policy-admin seed user
curl -s -X POST \
  "http://localhost:8180/realms/policy-management/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=policy-management-api" \
  -d "username=admin-user" \
  -d "password=${ADMIN_USER_PASSWORD}" \
  | jq -r '.access_token'

# Use the token in a request
TOKEN=$(curl -s ... | jq -r '.access_token')
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/v1/policies
```

**Rules:**
- Seed user credentials come from `.env` — never hard-coded in scripts or `realm-export.json`
- `realm-export.json` is committed to source control; it must contain no real passwords (Keycloak hashes them — use the Keycloak admin UI to set passwords post-import, or set via env at startup)
- `appsettings.Development.json` may contain the local Keycloak authority URL (non-secret) — it must not contain any credentials

---

## 8. Security Rules — What Must Never Happen

| Rule | Reason |
|---|---|
| Never log JWT token values | Tokens are credentials — logging them enables token replay attacks |
| Never log claim values (e.g., `sub`, role names) at `Information` or lower | Claim values can be PII or security-sensitive |
| Never expose auth errors with internal detail | `401`/`403` responses must use standard `ProblemDetails`; no `detail` or `extensions` that reveal realm or client configuration |
| Never commit Keycloak credentials to source control | Admin passwords, client secrets, and seed user passwords must only exist in `.env` (gitignored) or CI secret stores |
| Never hard-code realm name, client ID, or authority URL in C# source | Use `Keycloak__Authority` and `Keycloak__Audience` environment variables resolved via `KeycloakOptions` |
| Never disable auth globally for convenience | Do not set `FallbackPolicy` to anonymous; do not add a global `[AllowAnonymous]` filter |
| Never store tokens server-side in the BFF | The BFF is stateless — tokens are validated per-request; no session or token cache on the server |
| Never set `RoleClaimType` in `TokenValidationParameters` | Keycloak does not emit a flat `roles` claim; setting `RoleClaimType` to a non-existent claim silently breaks all role-based policy checks |
| Never skip `KeycloakRolesClaimsTransformation` registration | Without it, `realm_access.roles` is never mapped to `ClaimTypes.Role` and all `[Authorize(Policy = "...")]` checks that require roles return `403` |
| Never apply `[AllowAnonymous]` to `/api/v1/**` endpoints | All policy management endpoints require a valid Keycloak JWT — no exceptions |

---

## Enforcement Checklist

Before raising a PR that adds or modifies authentication, authorization, or protected endpoints:

- [ ] `KeycloakOptions` registered with `ValidateOnStart()` — app fails at startup if config is missing
- [ ] `RequireHttpsMetadata` is `true` in non-Development environments
- [ ] `RoleClaimType` is NOT set in `TokenValidationParameters`
- [ ] `KeycloakRolesClaimsTransformation` registered as `Transient` via `IClaimsTransformation`
- [ ] All `/api/v1/**` controller actions decorated with `[Authorize(Policy = "PolicyRead")]` or `[Authorize(Policy = "PolicyWrite")]`
- [ ] No controller action uses `[Authorize(Roles = "...")]`
- [ ] No controller action uses `[AllowAnonymous]`
- [ ] Middleware order is: `ExceptionHandlingMiddleware` → `CorrelationIdMiddleware` → `UseSerilogRequestLogging` → `UseAuthentication` → `UseAuthorization` → `MapControllers`
- [ ] Health check endpoints use `.AllowAnonymous()` on `MapHealthChecks` — not on a controller
- [ ] Integration tests use `ApiWebApplicationFactory` with `TestAuthHandler` — Keycloak is not started
- [ ] `403` test exists for every `PolicyWrite` endpoint using `_readerClient`
- [ ] No JWT token values or claim values logged at `Information` level or below
- [ ] No Keycloak credentials in source control — `.env` gitignored; `.env.example` committed
