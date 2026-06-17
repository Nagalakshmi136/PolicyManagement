# Copilot Instructions — PolicyManagement

## Project Overview

Backend REST API for insurance policy management, built as part of a Chubb APAC take-home assessment. The full requirements spec is in `docs/Chubb_APAC_Take-Home_Assessment_Backend.md`.

**Stack:** C# · .NET 10 · ASP.NET Core Web API · Entity Framework Core 9 · SQL Server · MediatR · FluentValidation · Serilog · Keycloak 24+ (JWT Bearer / OAuth2 PKCE) · xUnit · FluentAssertions · Moq · WebApplicationFactory · Docker

---

## Repository Layout

```
src/
├── PolicyManagement.Api/           # ASP.NET Core Web API — BFF entry point
├── PolicyManagement.Application/   # Use-cases, CQRS commands/queries, validators
├── PolicyManagement.Domain/        # Entities, enumerations, domain events, repository interfaces
└── PolicyManagement.Infrastructure/ # EF Core, repositories, migrations, seed data, external services

tests/
├── PolicyManagement.Domain.Tests/
├── PolicyManagement.Application.Tests/
├── PolicyManagement.Infrastructure.Tests/
└── PolicyManagement.Api.Tests/     # Integration tests via WebApplicationFactory

docs/
├── Chubb_APAC_Take-Home_Assessment_Backend.md  # Requirements spec
├── architecture.md                             # System architecture document
├── analysis/
│   └── policy-management-bff-analysis.md          # Requirements analysis, NFRs, acceptance criteria
├── openapi/
│   └── policy-management.yaml                     # OpenAPI 3.x spec — source of truth
└── adr/
    ├── ADR-001-clean-architecture-cqrs.md
    ├── ADR-002-sql-server-ef-core.md
    ├── ADR-003-contract-first-openapi.md
    ├── ADR-004-problemdetails-error-contract.md
    ├── ADR-005-url-path-versioning.md
    ├── ADR-006-imemorycache-caching.md
    ├── ADR-007-bulk-flag-partial-success.md
    ├── ADR-008-sort-parameter-convention.md
    ├── ADR-009-multicurrency-premium-grouping.md
    └── ADR-010-keycloak-jwt-bearer-authentication.md

keycloak/
└── realm-export.json               # Pre-configured realm, clients, roles, seed users

.github/
├── agents/                         # Custom Copilot agents
├── skills/                         # Coding standards and conventions
└── copilot-instructions.md
```

---

## Build & Run

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run the API
dotnet run --project src/PolicyManagement.Api

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~<TestClassName>.<TestMethodName>"

# Run tests in a specific project
dotnet test tests/PolicyManagement.Api.Tests

# Add a migration
dotnet ef migrations add <MigrationName> \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api \
  --output-dir Persistence/Migrations

# Apply migrations
dotnet ef database update \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api

# Run with Docker
docker-compose up --build
```

---

## Architecture

**Pattern:** Clean Architecture (Onion) — dependencies point inward only.

```
PolicyManagement.Api            → PolicyManagement.Application
PolicyManagement.Application    → PolicyManagement.Domain
PolicyManagement.Infrastructure → PolicyManagement.Domain
PolicyManagement.Infrastructure → PolicyManagement.Application   (interface contracts only)
PolicyManagement.Api            → PolicyManagement.Infrastructure (Program.cs DI registration only)
```

No other cross-project references are permitted. Domain has zero NuGet / framework dependencies. Application must not reference `Microsoft.AspNetCore.*` or EF Core types.

**Key patterns:**
- CQRS via MediatR — every use-case is a Command or Query with a dedicated handler
- Repository + Unit of Work — handlers never touch `DbContext` directly
- Global `ExceptionHandlingMiddleware` — all errors become RFC 9457 `ProblemDetails`
- OpenAPI-first — the spec in `docs/openapi/` is the source of truth; code derives from it
- Options pattern with `ValidateOnStart()` — misconfiguration fails at startup
- JWT Bearer auth via Keycloak — `AddJwtBearer` validates tokens; `KeycloakRolesClaimsTransformation` maps roles; named authorization policies (`PolicyRead`, `PolicyWrite`) on controllers via `[Authorize(Policy = "...")]`

---

## Key Conventions

| Concern | Standard |
|---|---|
| Layer boundaries & folder structure | `.github/skills/clean-architecture.md` |
| API contract design, versioning, pagination | `.github/skills/contract-first-api.md` |
| CQRS, MediatR, pipeline behaviours | `.github/skills/cqrs-mediator.md` |
| EF Core, SQL Server, migrations, repositories | `.github/skills/database-conventions.md` |
| Error handling, ProblemDetails, exception types | `.github/skills/error-handling.md` |
| Logging, health checks, Docker, caching | `.github/skills/production-readiness.md` |
| xUnit, FluentAssertions, Moq, integration tests | `.github/skills/testing-standards.md` |
| Authentication, Keycloak, JWT Bearer, RBAC | `.github/skills/auth-standards.md` |

Always read the relevant skill file before implementing in that area.

---

## Domain Context

Core domain entity: **Policy** — an insurance contract. It is the sole aggregate root; there is no separate `Claim` entity and no separate `Customer` entity — `policyholderName` is a plain field on `Policy`.

| Field | C# Type | Constraints / Notes |
|---|---|---|
| `id` | `string` | UUID string; surrogate primary key |
| `policyNumber` | `string` | Unique business key; format `POL-XXXXXX` |
| `policyholderName` | `string` | Name of the insured party (APAC-appropriate names in seed data) |
| `lineOfBusiness` | `LineOfBusiness` (enum) | `Property`, `Casualty`, `A&H`, `Marine` |
| `status` | `PolicyStatus` (enum) | `Active`, `Expired`, `Pending`, `Cancelled` |
| `premiumAmount` | `decimal` | Range 1,000–5,000,000; stored as `decimal(18,2)`; never `float` |
| `currency` | `string` | ISO 4217; one of: `USD`, `SGD`, `HKD`, `AUD`, `JPY`, `THB` |
| `effectiveDate` | `DateOnly` | Coverage start date |
| `expiryDate` | `DateOnly` | Coverage end date |
| `region` | `string` | One of: Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines |
| `underwriter` | `string` | Assigned underwriter name |
| `flaggedForReview` | `bool` | Default: `false`; set via `Policy.FlagForReview()` method only |
| `createdAt` | `DateTime` | UTC; set by `AuditSaveChangesInterceptor` on insert — never by the API consumer |
| `updatedAt` | `DateTime` | UTC; set by `AuditSaveChangesInterceptor` on update — never by the API consumer |

### Domain Behaviour Methods

| Method | Rule enforced |
|---|---|
| `FlagForReview()` | No-op if already flagged (idempotent); raises `PolicyFlaggedForReviewEvent` |

Refer to `docs/architecture.md` for the full domain model and `docs/Chubb_APAC_Take-Home_Assessment_Backend.md` for authoritative requirements.

---

## API Endpoints

| Method | Path | Authorization policy | Purpose |
|---|---|---|---|
| `GET` | `/api/v1/policies` | `PolicyRead` | Paginated list with filter, sort, search |
| `GET` | `/api/v1/policies/summary` | `PolicyRead` | Aggregated stats (counts by status, premium by LoB+currency, expiring-soon) |
| `GET` | `/api/v1/policies/{id}` | `PolicyRead` | Single policy by surrogate ID |
| `PATCH` | `/api/v1/policies/flag` | `PolicyWrite` | Bulk flag for review (partial-success semantics) |
| `GET` | `/health/live` | Anonymous | Liveness probe |
| `GET` | `/health/ready` | Anonymous | Readiness probe (DB connectivity) |

All `/api/v1/policies/**` endpoints require a valid Keycloak JWT. `PolicyRead` = `policy-reader` or `policy-admin`. `PolicyWrite` = `policy-admin` only. Both policies are defined once in `Program.cs` via `AddAuthorization()`.

---

## Error Contract

All errors use RFC 9457 `ProblemDetails` (`application/problem+json`) shaped by `ExceptionHandlingMiddleware`.

| Exception / condition | HTTP status | `errors` map |
|---|---|---|
| `ValidationException` (FluentValidation) | `400` | Yes — camelCase field keys |
| JWT missing / invalid / expired | `401` | No (emitted by `AddJwtBearer`) |
| Authorization policy fails | `403` | No |
| `NotFoundException` | `404` | No |
| `DomainException` | `422` | No |
| Unhandled `Exception` | `500` | No — `detail` is `null` (never leaked) |

---

## What Copilot Must Never Do

- Put business logic in controllers — logic belongs in domain entities or application handlers
- Touch `DbContext` directly in Application or API layers — always go through a repository interface
- Return EF Core entities from API endpoints — always map to a DTO or response record first
- Hardcode connection strings, secrets, or environment-specific values — use the Options pattern and environment variables
- Skip FluentValidation on incoming requests — every command and query must have a registered validator
- Skip structured logging in handlers or middleware — use `ILogger<T>` with named properties, never `Console.WriteLine`
- Write migrations manually — always use `dotnet ef migrations add`; never hand-edit generated migration files
- Expose stack traces, exception type names, or internal details in error responses — `500` responses must have a null `detail` field
- Use `Console.WriteLine` anywhere in production code — always use `ILogger<T>` with named properties
- Remove `[Authorize]` from protected endpoints or bypass JWT Bearer validation — `/api/v1/policies/**` endpoints require a valid Keycloak JWT
- Hardcode Keycloak credentials, realm names, or client secrets — use `Keycloak__Authority` and `Keycloak__Audience` environment variables
- Skip `TestAuthHandler` in integration tests — every `WebApplicationFactory`-based test must replace JWT Bearer with the test scheme and include role claims
- Use `[Authorize(Roles = "...")]` directly on controller actions — always use named authorization policies (`PolicyRead`, `PolicyWrite`) defined once in `Program.cs` via `AddAuthorization()`
- Set `RoleClaimType` in `TokenValidationParameters` — Keycloak does not emit a flat `roles` claim; `KeycloakRolesClaimsTransformation` handles role extraction from `realm_access.roles`

---

## Agents

| Agent | Invoke when |
|---|---|
| **Architect** | Designing layer structure, database schema, ADRs, dependency rules, BFF pattern decisions |
| **Product Analyst** | Analysing requirements, writing acceptance criteria, identifying NFRs and risks |
| **Backend Developer** | Implementing features, handlers, repositories, controllers, DTOs |
| **QA Engineer** | Writing unit tests, integration tests, identifying edge cases |
| **DevOps Engineer** | Dockerfile, docker-compose, GitHub Actions, health checks |
| **Reviewer** | Reviewing code for architecture, security, performance, test coverage |
| **OpenAPI Designer** | Designing or updating the OpenAPI spec, defining schemas, adding security schemes |
| **Commit Writer** | Generating conventional commit messages for staged changes |
| **PR Writer** | Generating pull request descriptions from branch commits |
