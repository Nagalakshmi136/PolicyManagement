# Requirements Analysis — Policy Management BFF Service

## Summary

This document analyses the requirements for the **Policy Management BFF (Backend-for-Frontend)** service defined in the Chubb APAC Take-Home Assessment (v1.0, May 2026). The service sits between the Policy Overview Dashboard front-end and downstream systems (SQL Server, optional cache, optional message broker). It must expose a contract-first RESTful API covering policy listing, retrieval, bulk flagging, and aggregated statistics, backed by a Clean Architecture implementation with production-quality cross-cutting concerns. Two optional bonus tracks — caching and Kafka event streaming — extend the core scope.

---

## Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | Expose `GET /api/v1/policies` returning a paginated list of policies | Must Have |
| FR-02 | Support pagination via `page` and `pageSize` query parameters (defaults: `page=1`, `pageSize=20`, max `pageSize=100`) | Must Have |
| FR-03 | Support sorting the policy list by any schema field with configurable direction (`asc` / `desc`) | Must Have |
| FR-04 | Filter the policy list by `status` (Active, Expired, Pending, Cancelled) | Must Have |
| FR-05 | Filter the policy list by `lineOfBusiness` (Property, Casualty, A&H, Marine) | Must Have |
| FR-06 | Filter the policy list by `region` | Must Have |
| FR-07 | Filter the policy list by `effectiveDateFrom` / `effectiveDateTo` date range | Must Have |
| FR-08 | Free-text `search` across `policyNumber`, `policyholderName`, and `underwriter` fields | Must Have |
| FR-09 | Expose `GET /api/v1/policies/{id}` returning a single policy by its surrogate ID | Must Have |
| FR-10 | Expose `PATCH /api/v1/policies/flag` accepting an array of policy IDs and setting `flaggedForReview = true` on each | Must Have |
| FR-11 | Expose `GET /api/v1/policies/summary` returning aggregated statistics: counts by status, total premium by line of business, and expiring-soon count | Must Have |
| FR-12 | Define an **OpenAPI 3.x specification** as the single source of truth before any implementation begins | Must Have |
| FR-13 | Implement a relational database schema with migrations; seed 200+ realistic policy records covering all statuses, lines of business, APAC regions, and a realistic spread of dates and premium amounts | Must Have |
| FR-14 | Apply Clean Architecture layering: API → Application → Domain ← Infrastructure, with no inward layer depending on an outward one | Must Have |
| FR-15 | Provide a runnable local development setup via `docker-compose up` | Must Have |
| FR-16 | Implement structured request/correlation logging and a health-check endpoint | Must Have |
| FR-17 | Externalise all configuration (connection strings, feature flags) using environment variables; no hardcoded secrets | Must Have |
| FR-18 | Implement a caching layer for summary statistics and/or policy listings with a defined invalidation strategy | Nice to Have |
| FR-19 | Implement a Kafka producer that publishes an event whenever policies are flagged for review | Nice to Have |
| FR-20 | Implement a Kafka consumer that handles incoming policy status-change events with idempotent processing and a well-defined event schema | Nice to Have |
| FR-21 | Maintain a committed AI working journal documenting accepted, challenged, and overridden AI suggestions with brief reasoning | Must Have |
| FR-22 | Protect all policy API endpoints (`/api/v1/policies/**`) with JWT Bearer authentication via Keycloak; health check endpoints (`/health/live`, `/health/ready`) remain anonymous | Must Have |
| FR-23 | Enforce role-based access control: `policy-reader` role grants read-only access; `policy-admin` role additionally permits `PATCH /api/v1/policies/flag`; requests with insufficient role return `403 Forbidden` | Must Have |

---

## Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-01 | API Contract | The OpenAPI 3.x spec in `docs/openapi/` must be the single source of truth; all schema objects defined under `components/schemas` using `$ref`; spec must pass Spectral lint with zero errors before any PR merge |
| NFR-02 | API Contract | Every path operation must declare `operationId` (unique, camelCase), `summary`, `tags`, `parameters`, and all expected response status codes |
| NFR-03 | API Versioning | All endpoints must be rooted at `/api/v{version}/`; URL-path versioning with `DefaultApiVersion = 1.0`; responses must include the `api-supported-versions` header |
| NFR-04 | Error Responses | All errors must use RFC 9457 `ProblemDetails` (`application/problem+json`); `500` responses must have a `null` `detail` field and must never expose stack traces, connection strings, or internal type names |
| NFR-05 | Error Responses | `400 Bad Request` must include a camelCase-keyed `errors` map; `404`, `422`, and `500` must not include an `errors` map |
| NFR-06 | Error Responses | `400` is strictly for structural / validation failures; `422` is strictly for business rule violations; never conflate the two |
| NFR-07 | Pagination | `pageSize` exceeding 100 must return `400 Bad Request`; a `page` value beyond `totalPages` must return an empty `data` array with accurate `pagination` metadata — not a `404` |
| NFR-08 | Data Integrity | `premiumAmount` must be stored as `decimal(18,2)`; never `float` or `double` |
| NFR-09 | Data Integrity | `policyNumber` must be unique; format enforced as `POL-XXXXXX` (6 alphanumeric characters after the prefix) |
| NFR-10 | Data Integrity | `currency` must be a valid ISO 4217 code restricted to: USD, SGD, HKD, AUD, JPY, THB |
| NFR-11 | Data Integrity | `region` must be one of the eight defined APAC values: Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines |
| NFR-12 | Security | Connection strings and secrets must be supplied via environment variables and never committed to source control |
| NFR-13 | Security | The service must not leak internal exception details, type names, or stack traces in any HTTP response |
| NFR-14 | Observability | All application events must use structured logging (`ILogger<T>` with named properties); `Console.WriteLine` is forbidden in production code |
| NFR-15 | Observability | A correlation ID must be attached to every request log entry and surfaced in error responses as `traceId` |
| NFR-16 | Observability | A health-check endpoint must be available and must verify database connectivity |
| NFR-17 | Testability — Domain | All entity creation and state-transition invariants must have unit tests; domain tests must use no mocks and no database |
| NFR-18 | Testability — Application | All command and query handlers must have unit tests with mocked repositories; validators must have dedicated test classes asserting each rule independently |
| NFR-19 | Testability — Infrastructure | Repository `AddAsync`, `UpdateAsync`, and query/filter operations must be tested against EF Core InMemory; filtering and pagination must be verified |
| NFR-20 | Testability — API | Each endpoint must have integration tests via `WebApplicationFactory` covering: happy path, `400` validation failure, `404` not-found, `422` business-rule violation, pagination boundary cases, `401` unauthenticated access, and `403` insufficient-role access |
| NFR-21 | Testability — Naming | Test methods must follow the pattern `<MethodOrScenario>_<StateOrInput>_<ExpectedOutcome>` using underscores; no abbreviations |
| NFR-22 | Code Quality | All code must adhere to SOLID and DRY principles; business logic belongs in domain entities or application handlers — never in controllers |
| NFR-23 | Deployability | The service must start successfully via `docker-compose up --build`; the Docker image must not expose secrets via environment variable defaults |
| NFR-24 | Configuration | All configurable values must use the Options pattern with `ValidateOnStart()`; misconfiguration must fail at startup, not at runtime |
| NFR-25 | Migrations | All schema changes must be managed via `dotnet ef migrations add`; hand-edited migration files are forbidden |
| NFR-26 | Caching (Bonus) | Cached entries for summary statistics must be invalidated when any policy is created, updated, flagged, or its status changes |
| NFR-27 | Kafka (Bonus) | Kafka consumer must be idempotent — processing the same event twice must produce no duplicate side effects |
| NFR-28 | Security — Authentication | All `/api/v1/policies/**` endpoints must require a valid JWT Bearer token issued by Keycloak; tokens are validated via the Keycloak JWKS endpoint; the BFF never issues tokens or handles credentials directly; see [ADR-010](../adr/ADR-010-keycloak-jwt-bearer-authentication.md) |
| NFR-29 | Security — RBAC | Two Keycloak realm roles (`policy-reader`, `policy-admin`) must be enforced via named authorization policies (`PolicyRead`, `PolicyWrite`) defined once in `Program.cs` and applied with `[Authorize(Policy = "...")]` on controller actions; roles are extracted from the JWT `realm_access.roles` claim via `KeycloakRolesClaimsTransformation`; `RoleClaimType` must **not** be set in `TokenValidationParameters` (Keycloak does not emit a flat `roles` claim); token validation and role enforcement must be covered by integration tests using `TestAuthHandler` |
| NFR-30 | Security — Auth Config | Keycloak `Authority` and `Audience` must be supplied via environment variables (`Keycloak__Authority`, `Keycloak__Audience`) with `ValidateOnStart()`; the Keycloak admin password must be in `.env` (gitignored), never hardcoded |

---

## Risks & Assumptions

| ID | Type | Description | Mitigation |
|---|---|---|---|
| R-01 | Risk | Route conflict: `GET /api/v1/policies/{id}` and `GET /api/v1/policies/summary` / `PATCH /api/v1/policies/flag` will conflict if the router treats `"summary"` and `"flag"` as `{id}` values. Framework default routing resolves literal segments before parameterised ones in most cases, but this must be explicitly verified and tested. | Declare literal routes (`/summary`, `/flag`) before the `{id}` parameterised route in the controller; add integration tests that assert the correct handler is invoked for each path. |
| R-02 | Risk | The sort parameter format in the requirements spec (`sort=premiumAmount,desc`) differs from the two-parameter convention in the project's contract-first API skill (`sortBy` + `sortDirection`). Using the comma-delimited single parameter may complicate OpenAPI spec definition and client consumption. | Clarify with the hiring panel before implementing; if ambiguous, adopt the two-parameter convention from the skill file as it is more OpenAPI-idiomatic and consistent with the existing standard. |
| R-03 | Risk | The `GET /api/v1/policies/summary` endpoint returns `totalPremiumByLineOfBusiness` but policies span six different currencies (USD, SGD, HKD, AUD, JPY, THB). It is unclear whether summation should be per-currency, converted to a single base currency, or simply summed as raw numbers. Cross-currency aggregation without a conversion rate source would produce misleading figures. | Surface this ambiguity before implementing the summary endpoint; default behaviour should group by both `lineOfBusiness` and `currency` to avoid misleading aggregates, unless directed otherwise. |
| R-04 | Risk | The definition of "expiring soon" for the `expiringSoonCount` in the summary response is not specified. Without a fixed threshold, the metric cannot be tested or agreed upon. | Assume a 30-day rolling window from the current date as the default; document this assumption in the OpenAPI spec description and in the AI working journal. |
| R-05 | Risk | The `PATCH /api/v1/policies/flag` endpoint accepts an array of IDs but the atomicity requirement is unspecified. If some IDs exist and others do not, the service must decide between: (a) atomic — reject all or commit all; (b) partial success — flag what exists, report what was not found. Either choice affects the response contract. | Default to partial-success semantics (flag all matching IDs, return a response body enumerating any IDs that were not found) unless directed otherwise; define the response shape in the OpenAPI spec before implementing. |
| R-06 | ~~Risk~~ **Resolved** | ~~No authentication or authorisation mechanism is specified.~~ Authentication and authorisation are required for production deployment. | **Resolved 2026-06-16:** Keycloak 24+ adopted as the identity provider with OAuth2 Authorization Code + PKCE flow and JWT Bearer validation. Two RBAC roles defined: `policy-reader` (read) and `policy-admin` (read + flag). Integration tests use `TestAuthHandler` to bypass Keycloak. See [ADR-010](../adr/ADR-010-keycloak-jwt-bearer-authentication.md). |
| R-07 | Risk | Kafka bonus scope requires a running Kafka broker for local development and integration tests. This adds Docker Compose complexity and Testcontainers overhead. If time is limited, shipping Kafka without integration tests risks unverifiable event behaviour. | Treat Kafka as the last item in the build backlog; if time is insufficient for a testable implementation, document the intended design in an ADR rather than shipping untested producer/consumer code. |
| R-08 | Risk | The assessment hard cap is 5 hours. Delivering all four required layers (API, Application, Domain, Infrastructure) with full test coverage, seed data, Docker setup, OpenAPI spec, and an AI working journal within that window is aggressive. | Prioritise in this order: (1) OpenAPI spec + domain model, (2) core CRUD endpoints, (3) filtering/pagination, (4) summary + bulk flag, (5) cross-cutting concerns, (6) caching, (7) Kafka. |
| A-01 | Assumption | SQL Server (via Docker) is the target database for local development, matching the OneHub production stack. | N/A |
| A-02 | Assumption | The `id` field is a UUID string (surrogate key); `policyNumber` (`POL-XXXXXX`) is the human-facing business key. URL paths use the surrogate `id`, not `policyNumber`. | N/A |
| A-03 | Assumption | `createdAt` and `updatedAt` are always UTC and are set/updated by the infrastructure layer, never by the API consumer. | N/A |
| A-04 | Assumption | The `flaggedForReview` flag is a one-way toggle in the scope of this assessment — policies can be flagged but not un-flagged via the BFF. | N/A |
| A-05 | Assumption | The front-end will consume the `PagedResponse<PolicySummary>` shape directly; no additional BFF aggregation of multiple downstream services is required for this assessment. | N/A |
| A-06 | Assumption | "Realistic APAC names" for `policyholderName` in the seed data means culturally appropriate names for the eight specified APAC regions; no PII compliance obligations apply to seed data. | N/A |
| A-07 | Assumption | Two Keycloak realm roles (`policy-reader`, `policy-admin`) are sufficient for the current endpoint set. If finer-grained permissions are required (e.g., per-region access), role definitions can be extended in Keycloak without BFF code changes. | N/A |
| A-08 | Assumption | Keycloak runs in `start-dev` mode for local development and integration testing. Production deployment requires `start` mode with a dedicated database backend and HTTPS — this is documented in ADR-010 as a production-hardening note outside the assessment scope. | N/A |

---

## Acceptance Criteria

### FR-01 / FR-02: Paginated Policy List

- **Given** the database contains 200 seeded policies  
  **When** `GET /api/v1/policies` is called with no query parameters  
  **Then** the response is `200 OK` with `Content-Type: application/json`, `data` contains exactly 20 items, and `pagination` reports `{ page: 1, pageSize: 20, totalCount: 200, totalPages: 10 }`

- **Given** a valid request  
  **When** `GET /api/v1/policies?page=2&pageSize=10` is called  
  **Then** `data` contains 10 items from offset 10 and `pagination.page` is `2`

- **Given** a request with `pageSize` exceeding 100  
  **When** `GET /api/v1/policies?pageSize=101` is called  
  **Then** the response is `400 Bad Request` with `Content-Type: application/problem+json`, `status: 400`, `title: "Bad Request"`, and an `errors` map containing a `pageSize` key

- **Given** a request with `page` beyond `totalPages`  
  **When** `GET /api/v1/policies?page=9999` is called  
  **Then** the response is `200 OK`, `data` is an empty array, and `pagination` reflects accurate `totalCount` and `totalPages`

---

### FR-03: Sorting

- **Given** a valid request  
  **When** `GET /api/v1/policies?sortBy=premiumAmount&sortDirection=desc` is called  
  **Then** the `data` array is ordered by `premiumAmount` descending

- **Given** a request with an unrecognised sort field  
  **When** `GET /api/v1/policies?sortBy=unknownField` is called  
  **Then** the response is `400 Bad Request` with `errors.sortBy` present

---

### FR-04 / FR-05 / FR-06: Enum and Region Filtering

- **Given** seeded policies with mixed statuses  
  **When** `GET /api/v1/policies?status=Active` is called  
  **Then** all items in `data` have `status: "Active"` and `pagination.totalCount` reflects only active policies

- **Given** a request with an invalid status value  
  **When** `GET /api/v1/policies?status=Invalid` is called  
  **Then** the response is `400 Bad Request` with `errors.status` present

- **Given** seeded policies across regions  
  **When** `GET /api/v1/policies?region=Singapore` is called  
  **Then** all items in `data` have `region: "Singapore"`

---

### FR-07: Date Range Filtering

- **Given** seeded policies with varied effective dates  
  **When** `GET /api/v1/policies?effectiveDateFrom=2024-01-01&effectiveDateTo=2024-12-31` is called  
  **Then** all items in `data` have `effectiveDate` between `2024-01-01` and `2024-12-31` inclusive

- **Given** a request where `effectiveDateFrom` is after `effectiveDateTo`  
  **When** the request is submitted  
  **Then** the response is `400 Bad Request` with an `errors` map referencing the date range fields

---

### FR-08: Free-Text Search

- **Given** a policy with `policyholderName: "Tanaka Hiroshi"` exists  
  **When** `GET /api/v1/policies?search=Tanaka` is called  
  **Then** the response includes that policy in `data`

- **Given** a `search` value that matches no policies  
  **When** `GET /api/v1/policies?search=zzznomatch` is called  
  **Then** the response is `200 OK` with an empty `data` array

---

### FR-09: Get Policy by ID

- **Given** a policy with id `{id}` exists  
  **When** `GET /api/v1/policies/{id}` is called  
  **Then** the response is `200 OK` with a `PolicyResponse` body containing all 14 schema fields

- **Given** no policy exists with id `nonexistent-id`  
  **When** `GET /api/v1/policies/nonexistent-id` is called  
  **Then** the response is `404 Not Found` with `Content-Type: application/problem+json`, `status: 404`, `title: "Not Found"`, a non-null `detail` field, a `traceId` extension, and no `errors` map

---

### FR-10: Bulk Flag for Review

- **Given** two policies with ids `[id-A, id-B]` exist and have `flaggedForReview: false`  
  **When** `PATCH /api/v1/policies/flag` is called with body `{ "policyIds": ["id-A", "id-B"] }`  
  **Then** the response is `200 OK` and subsequent `GET /api/v1/policies/{id-A}` and `GET /api/v1/policies/{id-B}` return `flaggedForReview: true`

- **Given** an empty `policyIds` array is submitted  
  **When** `PATCH /api/v1/policies/flag` is called with body `{ "policyIds": [] }`  
  **Then** the response is `400 Bad Request` with `errors.policyIds` present

- **Given** an array containing at least one non-existent ID  
  **When** `PATCH /api/v1/policies/flag` is called  
  **Then** the response body enumerates the IDs that were not found; existing IDs are still flagged (partial-success semantics per assumption A-05 variant)

---

### FR-11: Summary Statistics

- **Given** the database contains policies across all statuses and lines of business  
  **When** `GET /api/v1/policies/summary` is called  
  **Then** the response is `200 OK` with a body containing: `countsByStatus` (one entry per status value), `totalPremiumByLineOfBusiness` (one entry per line of business, grouped by currency per R-03), and `expiringSoonCount` (policies with `expiryDate` within 30 days of the request date, per assumption A-04 default)

---

### FR-12: OpenAPI Specification

- **Given** the spec file at `docs/openapi/policy-management.yaml`  
  **When** Spectral lint is run against it  
  **Then** zero errors are reported

- **Given** the spec defines all four endpoints  
  **When** each path operation is inspected  
  **Then** every operation has `operationId` (unique, camelCase), `summary`, `tags`, and all expected response status codes defined with `$ref` schemas

---

### FR-13: Database Seeding

- **Given** the service starts against an empty database  
  **When** migrations are applied and seeding runs  
  **Then** the database contains at least 200 policy rows covering all four `status` values, all four `lineOfBusiness` values, all eight APAC `region` values, and all six `currency` codes

---

### FR-15: Local Docker Setup

- **Given** Docker and Docker Compose are installed  
  **When** `docker-compose up --build` is run from the repository root  
  **Then** the API is accessible at `http://localhost:{configured port}/api/v1/policies` and returns a `200 OK` response within 60 seconds of container start

---

### FR-16: Health Check & Logging

- **Given** the service is running  
  **When** `GET /health` is called  
  **Then** the response is `200 OK` and the body reports database connectivity status

- **Given** a request that results in any error  
  **When** the error is logged  
  **Then** the log entry contains structured fields for `Method`, `Path`, `StatusCode`, and `ExceptionType`; no raw stack trace text appears in the log output at Warning or below level

---

### FR-17: Configuration Externalisation

- **Given** the service is started without a database connection string environment variable  
  **When** application startup runs  
  **Then** the process exits with a configuration validation error before accepting any HTTP traffic (ValidateOnStart behaviour)

---

### FR-18 (Bonus): Caching

- **Given** the summary cache is populated  
  **When** a policy is flagged or its status is changed  
  **Then** the cached summary entry is invalidated and the next `GET /api/v1/policies/summary` request reflects the updated data

---

### FR-22 / FR-23: Authentication and RBAC

- **Given** a request is made to `GET /api/v1/policies` with no `Authorization` header  
  **When** the request reaches the BFF  
  **Then** the response is `401 Unauthorized` with `Content-Type: application/problem+json`, a `WWW-Authenticate: Bearer` header, and no policy data in the body

- **Given** a valid JWT is presented but the user holds only the `policy-reader` role  
  **When** `PATCH /api/v1/policies/flag` is called  
  **Then** the response is `403 Forbidden` with `Content-Type: application/problem+json` and `status: 403`

- **Given** an expired JWT token is presented  
  **When** any protected endpoint is called  
  **Then** the response is `401 Unauthorized`

- **Given** a valid JWT with the `policy-admin` role is presented  
  **When** `PATCH /api/v1/policies/flag` is called with a valid body  
  **Then** the response is `200 OK` and policies are flagged

- **Given** `GET /health/live` is called with no `Authorization` header  
  **When** the request reaches the BFF  
  **Then** the response is `200 OK` (health endpoints are anonymous)

- **Given** the `Keycloak__Authority` environment variable is missing at startup  
  **When** the application starts  
  **Then** the process exits with a configuration validation error before accepting any HTTP traffic

---

### FR-19 / FR-20 (Bonus): Kafka Events

- **Given** a Kafka broker is available and the producer is configured  
  **When** `PATCH /api/v1/policies/flag` successfully flags one or more policies  
  **Then** one `PolicyFlaggedForReview` event per flagged policy is published to the configured topic with a schema containing at minimum: `policyId`, `flaggedAt` (UTC), `eventId` (unique per event)

- **Given** the Kafka consumer receives a `PolicyStatusChanged` event with an `eventId` it has already processed  
  **When** the consumer attempts to process the duplicate  
  **Then** no additional state change is applied (idempotent handling)
