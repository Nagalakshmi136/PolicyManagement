# ADR-010: Keycloak as the Identity Provider with OAuth2 Authorization Code Flow + PKCE

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect
- **Supersedes:** Risk R-06 in `docs/analysis/policy-management-bff-analysis.md` (auth assumed out of scope)

---

## Context

The Policy Management BFF is a production-level service for an insurance operations team. Unauthenticated access to policy data — which includes policyholder names, premium amounts, and coverage dates — violates basic data-protection principles and would fail any security review. Authentication and authorisation are non-negotiable for production deployment.

**Constraints:**
- Zero budget for SaaS identity providers
- Must be self-hosted (no internet dependency for token issuance)
- Must run within the existing Docker Compose stack without new paid infrastructure
- Must not require building a custom auth server (DIY JWT issuance is high-risk in an insurance context)
- The BFF code must not take a hard dependency on any specific identity provider library — only on standard JWT Bearer middleware

---

## Decision

Use **Keycloak 24+** as the identity provider (IdP), deployed as a Docker service alongside the existing API and SQL Server services. The BFF validates **JWT Bearer tokens** issued by Keycloak using ASP.NET Core's standard `Microsoft.AspNetCore.Authentication.JwtBearer` middleware. Keycloak is the only component that issues, refreshes, and revokes tokens. The BFF never handles credentials directly.

### Authentication Flow: OAuth2 Authorization Code + PKCE

Used by the Policy Overview Dashboard SPA (front-end).

```
┌─────────────────────────────────────────────────────────────────┐
│  1. User navigates to Policy Overview Dashboard (SPA)           │
│     SPA has no valid access token → must authenticate           │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  2. SPA generates code_verifier + code_challenge (PKCE)         │
│     Redirects browser to Keycloak:                              │
│     GET /realms/policy-mgmt/protocol/openid-connect/auth        │
│       ?response_type=code                                       │
│       &client_id=policy-dashboard                               │
│       &redirect_uri=https://dashboard.internal/callback         │
│       &scope=openid profile email roles                         │
│       &code_challenge=<sha256(code_verifier)>                   │
│       &code_challenge_method=S256                               │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  3. Keycloak renders login page                                 │
│     User enters credentials (username + password)              │
│     (Keycloak handles all credential storage and validation)    │
└───────────────────────┬─────────────────────────────────────────┘
                        │ Auth code issued
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  4. Keycloak redirects browser to:                              │
│     https://dashboard.internal/callback?code=<auth_code>       │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  5. SPA exchanges code for tokens (back-channel to Keycloak):   │
│     POST /realms/policy-mgmt/protocol/openid-connect/token      │
│       client_id=policy-dashboard                                │
│       grant_type=authorization_code                             │
│       code=<auth_code>                                          │
│       redirect_uri=https://dashboard.internal/callback          │
│       code_verifier=<original_verifier>  ← PKCE proof           │
│                                                                 │
│     Response: { access_token, refresh_token, id_token }        │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  6. SPA calls BFF with Bearer token:                            │
│     GET /api/v1/policies                                        │
│     Authorization: Bearer <access_token>                        │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  7. BFF validates JWT (AddJwtBearer middleware):                │
│     a. Fetch Keycloak's JWKS endpoint (cached, auto-refreshed)  │
│     b. Verify JWT signature using public key                    │
│     c. Validate: issuer, audience, expiry, not-before           │
│     d. Extract claims (sub, email, realm_access.roles)         │
│     e. Evaluate named authorization policy on the endpoint      │
│     f. If valid → dispatch MediatR command/query                │
│     g. If invalid → 401 Unauthorized (ProblemDetails)           │
└─────────────────────────────────────────────────────────────────┘
```

PKCE (Proof Key for Code Exchange) prevents authorisation code interception attacks, which is critical for browser-based SPAs that cannot store a client secret securely.

---

## Role-Based Access Control (RBAC)

Define two Keycloak realm roles:

| Keycloak Role | Permissions | Endpoints |
|---|---|---|
| `policy-reader` | Read-only access | `GET /api/v1/policies`, `GET /api/v1/policies/{id}`, `GET /api/v1/policies/summary` |
| `policy-admin` | Read + flag for review | All `policy-reader` endpoints + `PATCH /api/v1/policies/flag` |

Roles are embedded in the JWT `realm_access.roles` claim by Keycloak. `KeycloakRolesClaimsTransformation` reads that nested structure and maps each role to a `ClaimTypes.Role` claim on the principal. Named authorization policies then evaluate those claims.

---

## Infrastructure Changes

### 1. Docker Compose — add Keycloak service

```yaml
# docker-compose.yml
services:
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

  api:
    # ... existing config ...
    depends_on:
      db:
        condition: service_healthy
      keycloak:
        condition: service_healthy
    environment:
      - Keycloak__Authority=http://keycloak:8080/realms/policy-mgmt
      - Keycloak__Audience=policy-management-api
```

### 2. .env.example — add Keycloak variables

```env
SA_PASSWORD=YourStrong@Password123
KEYCLOAK_ADMIN=admin
KEYCLOAK_ADMIN_PASSWORD=YourAdminPassword123
```

### 3. Keycloak Realm Export (`keycloak/realm-export.json`)

A pre-configured realm JSON is committed to the repository that creates on first start:
- Realm: `policy-mgmt`
- Client: `policy-dashboard` (public client, PKCE, CORS allowed for front-end origin)
- Client: `policy-management-api` (bearer-only, audience validator)
- Roles: `policy-reader`, `policy-admin`
- One seed user per role for local development (credentials in `.env`, not hardcoded)

---

## Application Code Changes

### 4. Options class — `PolicyManagement.Api`

```csharp
// Api/Options/KeycloakOptions.cs
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    [Required, Url]
    public string Authority { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;
}
```

### 5. JWT Bearer registration — `Program.cs`

```csharp
// Program.cs
var keycloakOptions = builder.Configuration
    .GetSection(KeycloakOptions.SectionName)
    .Get<KeycloakOptions>()!;

builder.Services
    .AddOptions<KeycloakOptions>()
    .BindConfiguration(KeycloakOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority  = keycloakOptions.Authority;   // Keycloak realm URL
        options.Audience   = keycloakOptions.Audience;    // policy-management-api
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            // Do NOT set RoleClaimType here.
            // KeycloakRolesClaimsTransformation (section 6) reads realm_access.roles
            // and adds them as ClaimTypes.Role claims on the principal.
            // A flat "roles" claim does not exist in the Keycloak JWT.
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.Response.Headers["WWW-Authenticate"] =
                    $"Bearer realm=\"{keycloakOptions.Authority}\"";
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // All read endpoints — policy-reader or policy-admin
    options.AddPolicy("PolicyRead", policy =>
        policy.RequireRole("policy-reader", "policy-admin"));

    // Mutating endpoints — policy-admin only
    options.AddPolicy("PolicyWrite", policy =>
        policy.RequireRole("policy-admin"));
});

// Middleware order (auth after exception handler):
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();   // ← validate JWT
app.UseAuthorization();    // ← enforce [Authorize] policies
app.MapControllers();
```

### 6. Claims transformation — map Keycloak nested roles

Keycloak embeds roles as `realm_access: { roles: [...] }` in the JWT payload. ASP.NET Core's default claim extraction does not flatten this nested structure. A `ClaimsTransformation` is needed:

```csharp
// Api/Security/KeycloakRolesClaimsTransformation.cs
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = (ClaimsIdentity)principal.Identity!;

        // Keycloak places realm roles at: realm_access.roles (JSON array)
        var realmAccessClaim = identity.FindFirst("realm_access");
        if (realmAccessClaim is null) return Task.FromResult(principal);

        using var doc = JsonDocument.Parse(realmAccessClaim.Value);
        if (!doc.RootElement.TryGetProperty("roles", out var rolesElement)) 
            return Task.FromResult(principal);

        foreach (var role in rolesElement.EnumerateArray())
        {
            var roleName = role.GetString();
            if (!string.IsNullOrEmpty(roleName) && !identity.HasClaim(ClaimTypes.Role, roleName))
                identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
        }

        return Task.FromResult(principal);
    }
}

// Register in Program.cs:
builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();
```

### 7. Controller — named authorization policies

Policies are defined once in `Program.cs` (section 5). Controllers reference them by name. When role membership changes, only the policy definition in `Program.cs` needs updating.

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/policies")]
[Authorize]   // ← all actions require a valid token (at minimum)
public class PoliciesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> List([FromQuery] ListPoliciesQuery query, CancellationToken ct) { ... }

    [HttpGet("summary")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> Summary(CancellationToken ct) { ... }

    [HttpGet("{id}")]
    [Authorize(Policy = "PolicyRead")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct) { ... }

    [HttpPatch("flag")]
    [Authorize(Policy = "PolicyWrite")]   // ← elevated policy required
    public async Task<IActionResult> Flag([FromBody] FlagPoliciesRequest request, CancellationToken ct) { ... }
}
```

Health check endpoints are explicitly excluded from auth:
```csharp
app.MapHealthChecks("/health/live",  ...).AllowAnonymous();
app.MapHealthChecks("/health/ready", ...).AllowAnonymous();
```

### 8. OpenAPI spec — security scheme

```yaml
# docs/openapi/policy-management.yaml
components:
  securitySchemes:
    openIdConnect:
      type: openIdConnect
      openIdConnectUrl: http://localhost:8180/realms/policy-mgmt/.well-known/openid-configuration

security:
  - openIdConnect: []

paths:
  /api/v1/policies/flag:
    patch:
      security:
        - openIdConnect: [policy-admin]   # elevated scope requirement documented
```

---

## Test Changes

### Integration test `TestAuthHandler` (no Keycloak needed in tests)

```csharp
// tests/PolicyManagement.Api.Tests/Common/TestAuthHandler.cs
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name,    "test-user"),
            new Claim(ClaimTypes.NameIdentifier, "user-001"),
            new Claim(ClaimTypes.Role,    "policy-admin"),   // default test role = admin
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// Replace JwtBearer with TestAuth in ApiWebApplicationFactory:
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureTestServices(services =>
    {
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });
    });
}
```

Role-specific tests create separate `HttpClient` instances with restricted roles:

```csharp
// Test: policy-reader cannot flag policies
[Fact]
public async Task PATCH_Flag_AsReader_Returns403Forbidden()
{
    // Arrange — client with reader role only
    var client = _factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication("ReaderAuth")
                .AddScheme<AuthenticationSchemeOptions, ReaderTestAuthHandler>(
                    "ReaderAuth", _ => { });
        })).CreateClient();

    // Act
    var response = await client.PatchAsJsonAsync("/api/v1/policies/flag",
        new { policyIds = new[] { "some-id" } });

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

---

## Error Response for Auth Failures

The existing `ExceptionHandlingMiddleware` + ASP.NET Core auth middleware already handle these correctly:

| Scenario | HTTP status | Notes |
|---|---|---|
| No `Authorization` header | `401 Unauthorized` | Auth middleware short-circuits; returns `WWW-Authenticate` header |
| Expired JWT | `401 Unauthorized` | `AddJwtBearer` validates `exp` claim automatically |
| Valid JWT, insufficient role | `403 Forbidden` | Authorization policy evaluated after token validation |
| Valid JWT, `policy-admin` on flag endpoint | `200 OK` | Normal flow |

The `ExceptionHandlingMiddleware` already handles `UnauthorizedAccessException` → `403`. The `401` case is handled by the JWT Bearer middleware before the exception handler runs — ASP.NET Core emits a ProblemDetails-compatible `401` automatically when `AddProblemDetails()` is registered.

---

## Consequences

### Positive

- Zero credential handling in the BFF — the service never sees passwords or issues tokens
- Rotating signing keys is handled by Keycloak automatically; the BFF fetches the new JWKS on the next request
- Adding MFA, social login, or LDAP directory integration to Keycloak requires zero BFF code changes
- `AddJwtBearer` auto-discovers Keycloak's signing keys via the OIDC discovery document (`/.well-known/openid-configuration`) — no manual key management
- The BFF has no Keycloak SDK dependency — only the standard .NET `Microsoft.AspNetCore.Authentication.JwtBearer` package
- Integration tests use a `TestAuthHandler` that bypasses Keycloak entirely — test suite runs with no external service

### Negative

- Adds one Docker service to the Compose stack (~500MB RAM for Keycloak in `start-dev` mode)
- `start-dev` mode is not suitable for production; production Keycloak requires a proper database (not the embedded H2) and HTTPS — a PostgreSQL or SQL Server backend for Keycloak should be added for a real production deployment
- The realm export JSON must be kept in sync with Keycloak configuration changes
- Cold-start time increases by ~30 seconds while waiting for Keycloak to become healthy

### Production hardening (beyond assessment scope)

For a real production deployment beyond this assessment:
- Run Keycloak with `start` (not `start-dev`) and a SQL Server or PostgreSQL backend
- Enable HTTPS on Keycloak (`RequireHttpsMetadata = true` in `AddJwtBearer`)
- Enable Keycloak clustering (2+ replicas) for high availability
- Configure token lifetimes: access token 5–15 minutes, refresh token 24–48 hours for insurance operations
- Enable Keycloak's brute-force detection and rate limiting on the token endpoint

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Auth0 / Firebase / Supabase** (SaaS) | SaaS dependency introduces an external network requirement for every token validation (JWKS fetch, though cached); vendor lock-in; data residency concerns for insurance policyholder data |
| **ASP.NET Core Identity + hand-rolled JWT issuance** | You become responsible for implementing token issuance, refresh, revocation, and key rotation — all high-stakes cryptographic concerns; high implementation risk; maintenance burden |
| **Duende IdentityServer** | Requires a paid commercial licence for production use; free only for open-source or development use |
| **IdentityServer4** | End-of-life (security patches stopped); not acceptable for a production insurance system |
| **OpenIddict** | Valid alternative: free, embeds in the same .NET process, uses EF Core (same DB). Rejected in favour of Keycloak because Keycloak ships a full user management UI and handles realm/client/role configuration without writing additional C# code |
