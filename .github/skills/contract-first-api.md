---
applyTo: "src/PolicyManagement.Api/**,docs/**/*.yaml,docs/**/*.yml"
---

# Contract-First API Design Standards — PolicyManagement BFF

## Core Principle

The **OpenAPI 3.x specification is the single source of truth** for every API surface. The contract is authored first; C# code is derived from it. No endpoint, field, or error shape may exist in the implementation that is not described in the spec.

Workflow:
```
1. Define / update OpenAPI spec  →  docs/openapi/policy-management.yaml
2. Review contract with stakeholders (no code yet)
3. Implement controllers, DTOs, and validators to match the spec exactly
4. Validate the running API against the spec (Spectral, integration tests)
```

---

## OpenAPI Specification Location and Ownership

```
docs/
└── openapi/
    ├── policy-management.yaml      ← main spec (OpenAPI 3.x)
    └── components/                 ← reusable $ref targets
        ├── schemas/
        │   ├── Policy.yaml
        │   ├── Claim.yaml
        │   └── ...
        ├── responses/
        │   ├── BadRequest.yaml
        │   ├── NotFound.yaml
        │   └── ...
        └── parameters/
            ├── PageNumber.yaml
            ├── PageSize.yaml
            └── ...
```

**Rules:**
- All schema objects must be defined under `components/schemas` and referenced with `$ref` — never inlined inside path operations
- Every path operation requires: `summary`, `operationId`, `tags`, `parameters` (if any), `requestBody` (if applicable), and `responses`
- `operationId` must be unique across the entire spec and use `camelCase` (e.g., `createPolicy`, `getPolicyById`)
- The spec file is committed to source control and must pass Spectral lint with zero errors before any PR is merged

### Minimal Path Operation Template

```yaml
/policies/{policyId}:
  get:
    summary: Get a policy by ID
    operationId: getPolicyById
    tags: [Policies]
    parameters:
      - $ref: '#/components/parameters/PolicyId'
    responses:
      '200':
        description: Policy found
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PolicyResponse'
      '404':
        $ref: '#/components/responses/NotFound'
      '401':
        $ref: '#/components/responses/Unauthorized'
```

---

## Request and Response DTOs

### Naming Conventions

| Purpose | Suffix | Example |
|---|---|---|
| Create resource request body | `CreateXxxRequest` | `CreatePolicyRequest` |
| Update resource request body | `UpdateXxxRequest` | `UpdatePolicyRequest` |
| Partial update request body | `PatchXxxRequest` | `PatchClaimRequest` |
| Single resource response | `XxxResponse` | `PolicyResponse` |
| List item in a collection | `XxxSummary` | `PolicySummary` |
| Paginated collection wrapper | `PagedResponse<T>` | `PagedResponse<PolicySummary>` |

### DTO Placement

- API contracts (request/response models) live in `PolicyManagement.Api/Contracts/`
- Application DTOs (used inside use-case boundaries) live in `PolicyManagement.Application/{Feature}/`
- These are **two separate sets of types** — map between them in the controller or a dedicated mapper
- Never expose Application DTOs directly as HTTP response bodies; shape them for the client in the Api layer

### Mapping from Contract to Implementation

Every field in the OpenAPI schema must have a corresponding C# property. Use the same name (PascalCase in C#; camelCase produced automatically by `System.Text.Json`).

```csharp
// OpenAPI schema: CreatePolicyRequest
// properties: customerId (string), productCode (string), effectiveDate (date), coverageAmount (number)

public sealed record CreatePolicyRequest(
    string CustomerId,
    string ProductCode,
    DateOnly EffectiveDate,
    decimal CoverageAmount
);
```

**Rules:**
- Use `record` types for immutable request/response contracts
- Use `DateOnly` for date-only fields (`format: date` in OpenAPI), `DateTime` for date-time fields (`format: date-time`)
- Use `decimal` for monetary values — never `float` or `double`
- Nullable fields in the spec (`nullable: true` or `required` omission) map to nullable C# types (`string?`, `decimal?`)
- Required fields map to non-nullable types; rely on model validation to enforce presence

---

## Pagination and Filtering Conventions

### Query Parameters

All list endpoints use cursor-based or offset pagination. Default to **offset pagination** unless dataset size demands otherwise.

| Parameter | Type | Default | Max | Description |
|---|---|---|---|---|
| `page` | integer | `1` | — | 1-based page number |
| `pageSize` | integer | `20` | `100` | Items per page |
| `sortBy` | string | resource-defined | — | Field name to sort by |
| `sortDirection` | `asc` \| `desc` | `asc` | — | Sort direction |

Filtering uses explicit named query parameters (not a generic `filter=` string):

```
GET /policies?status=active&customerId=cust-123&effectiveDateFrom=2025-01-01&effectiveDateTo=2025-12-31
```

Define every filter parameter in the OpenAPI spec under `components/parameters`.

### Paginated Response Shape

```yaml
# components/schemas/PagedPolicyResponse
type: object
required: [data, pagination]
properties:
  data:
    type: array
    items:
      $ref: '#/components/schemas/PolicySummary'
  pagination:
    $ref: '#/components/schemas/PaginationMeta'

# components/schemas/PaginationMeta
type: object
required: [page, pageSize, totalCount, totalPages]
properties:
  page:
    type: integer
    example: 1
  pageSize:
    type: integer
    example: 20
  totalCount:
    type: integer
    example: 143
  totalPages:
    type: integer
    example: 8
```

Corresponding C# types:

```csharp
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Data,
    PaginationMeta Pagination
);

public sealed record PaginationMeta(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);
```

**Rules:**
- Always return `totalCount` and `totalPages` — clients must not need a second request to determine navigation bounds
- `pageSize` exceeding the maximum (100) returns `400 Bad Request` with a validation error
- An out-of-range `page` (beyond `totalPages`) returns an empty `data` array with accurate `pagination` meta — not a 404

---

## Error Response Shape

All errors use **RFC 9457 Problem Details** (`application/problem+json`). Never return plain strings or custom error envelopes.

### Standard Error Schema

```yaml
# components/schemas/ProblemDetails
type: object
required: [type, title, status]
properties:
  type:
    type: string
    format: uri
    example: "https://tools.ietf.org/html/rfc9110#section-15.5.5"
  title:
    type: string
    example: "Not Found"
  status:
    type: integer
    example: 404
  detail:
    type: string
    example: "Policy with ID 'POL-001' was not found."
  instance:
    type: string
    format: uri
    example: "/policies/POL-001"
  errors:
    type: object
    description: "Validation error map — field name → array of messages"
    additionalProperties:
      type: array
      items:
        type: string
    example:
      coverageAmount: ["Coverage amount must be greater than zero."]
      effectiveDate: ["Effective date must be in the future."]
```

### HTTP Status Code Mapping

| Scenario | Status | `title` |
|---|---|---|
| Input fails validation | `400` | `Bad Request` |
| Missing or invalid `Authorization` | `401` | `Unauthorized` |
| Authenticated but forbidden | `403` | `Forbidden` |
| Resource not found | `404` | `Not Found` |
| Business rule violation | `422` | `Unprocessable Entity` |
| Unexpected server fault | `500` | `Internal Server Error` |

**Rules:**
- `400` is reserved for **structural / validation** failures (missing required field, wrong type, constraint violation)
- `422` is for semantically valid requests that violate a **domain business rule** (e.g., cancelling an already-cancelled policy)
- `500` responses must never leak stack traces, connection strings, or internal type names
- The `errors` map is only present on `400` responses; omit it on `404`, `422`, `500`

### Global Exception Middleware (Api Layer)

```csharp
// Middleware maps typed exceptions → ProblemDetails
// DomainException          → 422 Unprocessable Entity
// NotFoundException        → 404 Not Found
// ValidationException      → 400 Bad Request  (+ errors map)
// Unhandled Exception      → 500 Internal Server Error
```

Implement in `PolicyManagement.Api/Middleware/ExceptionHandlingMiddleware.cs`. Never scatter `try/catch` into controllers.

---

## API Versioning Conventions

### Strategy: URL Path Versioning

```
/api/v1/policies
/api/v2/policies
```

URL versioning is preferred for the BFF pattern — it is explicit, cache-friendly, and easy to route.

### OpenAPI Spec Versioning

- Each major API version has its own spec file: `docs/openapi/v1/policy-management.yaml`, `docs/openapi/v2/policy-management.yaml`
- The `info.version` field in the spec reflects the **API version**, not a SemVer package version
- Minor, non-breaking additions (new optional fields, new endpoints) increment `info.version` as a minor bump (`1.1`, `1.2`) within the same file — no new spec file needed

### .NET Configuration

```csharp
// Program.cs
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true; // adds api-supported-versions header
});
```

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/policies")]
public class PoliciesController : ControllerBase { ... }
```

### Breaking vs Non-Breaking Changes

| Change | Classification | Action |
|---|---|---|
| Add optional request field | Non-breaking | Add to existing spec; no new version |
| Add optional response field | Non-breaking | Add to existing spec; no new version |
| Remove / rename field | **Breaking** | New major version (`v2`) |
| Change field type | **Breaking** | New major version |
| Change HTTP status code | **Breaking** | New major version |
| Add new endpoint | Non-breaking | Add to existing spec |
| Change URL path structure | **Breaking** | New major version |

**Rule:** A `v1` endpoint must never be silently altered in a breaking way. Deprecate with `deprecated: true` in the spec and the `Sunset` response header before removal.

---

## Validation Rules

### Two-Layer Validation

| Layer | Tool | Validates |
|---|---|---|
| API (request binding) | ASP.NET Core model binding + Data Annotations | Structural correctness: required fields present, string lengths, numeric ranges, enum values |
| Application (command) | FluentValidation | Business-level constraints: cross-field rules, format checks, domain-aware checks |

### API Layer — Data Annotations on Request Records

```csharp
public sealed record CreatePolicyRequest(
    [Required] string CustomerId,
    [Required, StringLength(20)] string ProductCode,
    [Required] DateOnly EffectiveDate,
    [Range(0.01, double.MaxValue, ErrorMessage = "Coverage amount must be greater than zero.")]
    decimal CoverageAmount
);
```

Return `400 Bad Request` with the standard `errors` map when model state is invalid. This is handled automatically if `[ApiController]` is applied.

### Application Layer — FluentValidation

```csharp
public sealed class CreatePolicyCommandValidator : AbstractValidator<CreatePolicyCommand>
{
    public CreatePolicyCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.EffectiveDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Effective date must be today or in the future.");

        RuleFor(x => x.CoverageAmount)
            .GreaterThan(0);
    }
}
```

Wire validators via MediatR pipeline behaviour (`ValidationBehaviour<TRequest, TResponse>`) so handlers never call `Validate()` manually.

### Validation Rules Must Mirror the OpenAPI Spec

Every constraint defined in the spec must be enforced in code, and vice versa.

| OpenAPI constraint | C# enforcement |
|---|---|
| `required: true` | `[Required]` or non-nullable record parameter + FluentValidation `NotEmpty()` |
| `maxLength: 50` | `[StringLength(50)]` or FluentValidation `MaximumLength(50)` |
| `minimum: 0.01` | `[Range(0.01, ...)]` or FluentValidation `GreaterThan(0)` |
| `pattern: "^POL-[0-9]+"` | FluentValidation `Matches(@"^POL-\d+$")` |
| `enum: [active, cancelled]` | C# `enum` type + `[JsonConverter(typeof(JsonStringEnumConverter))]` |
| `format: date` | `DateOnly` parameter type |
| `format: uuid` | `Guid` parameter type |

**Rule:** If a constraint exists only in one place (spec or code) it is a defect. Both must agree.

---

## Swashbuckle / Scalar Setup

The running API must serve an OpenAPI document that is kept in sync with the hand-authored spec.

```csharp
// Program.cs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PolicyManagement BFF",
        Version = "v1",
        Description = "Chubb APAC Policy Management Backend-for-Frontend API"
    });
    options.EnableAnnotations();
    // Include XML doc comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});
```

Annotate controllers with `[ProducesResponseType]` for every documented status code to keep the generated spec accurate:

```csharp
[HttpGet("{policyId}")]
[ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public async Task<IActionResult> GetById(string policyId, CancellationToken ct) { ... }
```

---

## Contract-First Enforcement Checklist

Before raising a PR that adds or modifies an endpoint:

- [ ] OpenAPI spec updated **before** (or alongside) the implementation — never after
- [ ] Every new schema object is defined under `components/schemas` and referenced with `$ref`
- [ ] Every new endpoint has `operationId`, `summary`, `tags`, and all expected response codes documented
- [ ] Request DTO field names and types match the spec exactly (names, nullability, formats)
- [ ] `[ProducesResponseType]` attributes on the controller match the spec's `responses` section
- [ ] All error responses use `ProblemDetails` (`application/problem+json`) — no custom envelopes
- [ ] `400` responses include the `errors` field map; `404`/`422`/`500` do not
- [ ] Validation constraints in code (Data Annotations + FluentValidation) match OpenAPI constraints
- [ ] Breaking changes increment the major API version; non-breaking changes do not
- [ ] Spectral lint passes with zero errors on the updated spec
