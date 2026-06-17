# ADR-004: RFC 9457 ProblemDetails as the Universal Error Contract

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

A production API must return consistent, machine-readable error responses so clients can handle failures programmatically. The service needs to distinguish at minimum between: invalid input (structural failures), missing resources, business rule violations, and unexpected server faults. Each category must carry sufficient information for clients to display appropriate messages and for operators to trace and diagnose issues.

Without a standard, error responses tend to be ad-hoc — sometimes plain strings, sometimes custom JSON envelopes — making client error handling fragile and inconsistent.

---

## Decision

Use **RFC 9457 ProblemDetails** (`application/problem+json`) as the universal error response format across all endpoints. All error shaping is centralised in `ExceptionHandlingMiddleware`; controllers and handlers never construct error responses directly.

Exception-to-status mapping:

| Exception | HTTP status | `detail` | `errors` |
|---|---|---|---|
| `FluentValidation.ValidationException` | `400` | `"One or more validation errors occurred."` | Present; camelCase field map |
| _(JWT Bearer middleware — not an exception)_ | `401` | JWT missing / invalid / expired | Absent |
| `NotFoundException` | `404` | Entity message | Absent |
| `ConflictException` | `409` | Exception message | Absent |
| `DomainException` | `422` | Exception message | Absent |
| `UnauthorizedAccessException` | `403` | Fixed message | Absent |
| `OperationCanceledException` | `499` | Absent | Absent |
| Any other `Exception` | `500` | `null` (never leaked) | Absent |

> **Note on 401:** The `401 Unauthorized` response is emitted by ASP.NET Core's `UseAuthentication` middleware (AddJwtBearer) when the JWT is missing, invalid, or expired. It is **not** thrown as an exception and therefore does not pass through `ExceptionHandlingMiddleware`. A `WWW-Authenticate: Bearer realm="..."` header is automatically added by the JWT Bearer handler. `AddProblemDetails()` ensures the 401 body conforms to `application/problem+json`.

`400` and `422` are strictly distinguished: `400` for structural/validation failures (missing fields, wrong type, constraint violations on input shape); `422` for semantically valid requests that violate domain business rules.

All error responses include a `traceId` extension field (`Activity.Current?.Id ?? HttpContext.TraceIdentifier`) for correlation.

---

## Consequences

### Positive

- Clients can parse errors uniformly using a single `ProblemDetails` deserialiser
- `400` responses include a field-keyed `errors` map that maps directly to form validation UI rendering
- `500` responses are guaranteed to never leak stack traces, connection strings, or internal type names
- `traceId` enables production incident correlation between client logs and server logs
- `ExceptionHandlingMiddleware` being the outermost middleware means no error escapes unformatted

### Negative

- Custom exception types (`NotFoundException`, `DomainException`, `ConflictException`) must be defined and maintained
- The `400` vs `422` distinction requires discipline — developers must know which layer is responsible for which class of validation
- `499` is not a standard HTTP status code; some proxies and load balancers may not handle it gracefully (though it is commonly used for client-disconnected scenarios)

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Custom error envelope** (e.g., `{ success: false, error: { code: "NOT_FOUND", message: "..." } }`) | Non-standard; front-end must implement a custom deserialiser; no ecosystem tooling support; violates the assessment's "production quality" expectation |
| **Plain string error bodies** | Unparseable by clients; no status-code semantic mapping; immediately rejected |
| **Returning `Result<T>` from handlers and mapping in controllers** | Valid alternative for eliminating exceptions in the happy path, but `Result<T>` types across all handlers add significant boilerplate; the exception + middleware approach is simpler and industry-standard for ASP.NET Core services |
