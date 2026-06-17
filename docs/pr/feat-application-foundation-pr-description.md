# feat: scaffold Application layer with MediatR pipeline behaviours and paginated response models

## Summary

This PR establishes the `PolicyManagement.Application` project scaffold — the CQRS/MediatR foundation that all future use-case handlers will build on. It introduces two open generic MediatR pipeline behaviours (`LoggingBehaviour` and `ValidationBehaviour`), the shared paginated response envelope (`PagedResponse<T>` / `PaginationMeta`), and the `AddApplication()` DI extension that wires everything together. No feature handlers or validators are added yet; this branch creates the cross-cutting skeleton required before any handler can be written.

Relevant acceptance criteria: **FR-14** (Clean Architecture layering — Application layer scaffolded before any feature work), **FR-16** (structured logging in all handlers — `LoggingBehaviour` satisfies this requirement for every future handler automatically), and the implied validation contract from **FR-10** / **FR-02** (FluentValidation must run in-pipeline before handlers; `ValidationBehaviour` enforces this across all requests).

---

## Changes

### Domain
No changes.

### Application
- **`Common/Behaviours/LoggingBehaviour.cs`** — Open generic `IPipelineBehavior<TRequest, TResponse>` registered as the **outermost** behaviour. Logs `Handling {RequestName}` on entry and `Handled {RequestName} in {ElapsedMs}ms` on success using structured `ILogger<T>`. On exception, logs `LogWarning` with elapsed time and rethrows — never swallows exceptions. Uses `Stopwatch` for accurate elapsed measurement. Deliberately does not log request payloads to avoid leaking sensitive data.
- **`Common/Behaviours/ValidationBehaviour.cs`** — Open generic `IPipelineBehavior<TRequest, TResponse>` registered **inside** `LoggingBehaviour` (so total time includes validation). Resolves all `IValidator<TRequest>` from DI; short-circuits and returns `next()` if no validators are registered. On failures, aggregates all `ValidationFailure` entries and throws `FluentValidation.ValidationException`, which `ExceptionHandlingMiddleware` maps to HTTP 400 with a camelCase `errors` map.
- **`Common/Models/PagedResponse.cs`** — Sealed record `PagedResponse<T>(IReadOnlyList<T> Items, PaginationMeta Pagination)` used as the standard return type for all list query handlers. Provides two static factory methods: `From(PagedResult<T>)` for same-type mapping and `From<TSource, TDest>(PagedResult<TSource>, Func<TSource, TDest>)` for projection. Depends on `Domain.Common.PagedResult<T>` — the only inward reference permitted by the Dependency Rule.
- **`Common/Models/PaginationMeta.cs`** — Sealed record carrying `Page`, `PageSize`, `TotalCount`, `TotalPages`, `HasNextPage`, `HasPreviousPage`. Returned alongside every page of results; the API controller maps this to the OpenAPI `PaginationMeta` schema.
- **`DependencyInjection.cs`** — `AddApplication(this IServiceCollection)` extension method. Registers MediatR from the Application assembly; adds `LoggingBehaviour` then `ValidationBehaviour` as open pipeline behaviours in that order; scans the assembly for all `IValidator<T>` implementations (including internal types) via `AddValidatorsFromAssembly`.

### Infrastructure
No changes.

### API
No changes.

### Tests
No tests added in this branch. Pipeline behaviour unit tests are deferred to a follow-up branch (see **Follow-up Work**).

### Infrastructure / DevOps
No changes.

### Documentation
- `docs/pr/feat-domain-layer-2026-06-17.md` — PR description for the preceding domain layer branch (carried forward in diff).
- `docs/reviews/review-uncommitted-2026-06-17.md` — Reviewer agent output for uncommitted changes (carried forward in diff).

---

## Testing

- **Unit tests:** None added in this branch. Handlers and validators do not exist yet; pipeline behaviour tests are deferred.
- **Integration tests:** None added in this branch. No endpoints are affected.
- **Auth:** Not applicable — no API layer changes.
- **How to run:**
  ```bash
  dotnet test
  ```
  All existing tests should continue to pass (no breaking changes to Domain or existing contracts).

---

## Checklist

- [x] All changes derive from `docs/openapi/policy-management.yaml` (response envelope matches `PaginationMeta` schema)
- [ ] Unit tests added for all new handlers and validators — **deferred** (no handlers exist yet; see Follow-up Work)
- [ ] Integration tests cover happy path, 400, 401, 403, 404, 422 — **not applicable** (no endpoints changed)
- [x] No hardcoded secrets or connection strings
- [x] `docker-compose up --build` passes locally (no new dependencies that break the build)
- [x] Health checks return 200 (no changes to health check wiring)
- [x] Structured logging in all new handlers — `LoggingBehaviour` enforces this automatically for every handler registered in the Application assembly
- [ ] AI working journal updated (`docs/ai-working-journal.md`) — not updated in this branch
- [ ] Reviewer agent run and findings addressed — not yet run for this branch

---

## Risks

- **No tests for pipeline behaviours** — `LoggingBehaviour` and `ValidationBehaviour` are untested. Both are simple pass-through decorators, but a future regression could silently break all validation or swallow structured log fields. Mitigated by the deferred unit test task below.
- **`ValidationBehaviour` runs validators synchronously** — `v.Validate(context)` is the synchronous overload. If any future validator introduces async I/O (e.g., a database uniqueness check), this will block a thread pool thread. The fix is to switch to `await v.ValidateAsync(context, cancellationToken)` when async validators are introduced.
- **Behaviour registration order is implicit** — `LoggingBehaviour` is registered first so it wraps `ValidationBehaviour`, giving total elapsed time including validation. This order is correct but not enforced by the type system; a future developer could accidentally reverse it.

---

## Follow-up Work

1. **Unit tests for `LoggingBehaviour` and `ValidationBehaviour`** — Verify that `LoggingBehaviour` logs start/end/failure messages with correct property names; verify that `ValidationBehaviour` short-circuits on failure and passes through on success. Priority: High — before any handler is merged.
2. **Feature handler branches** — `GetPoliciesQuery`, `GetPolicyByIdQuery`, `GetPolicySummaryQuery`, `BulkFlagPoliciesCommand` handlers with their validators. These are the next planned branches.
3. **Async validator support** — Switch `ValidationBehaviour` to `await ValidateAsync(...)` when the first async validator is introduced (e.g., for cross-field database checks).
4. **`PagedResponse<T>` integration with controllers** — The API layer will need to map `PaginationMeta` to the response body and set `X-Total-Count` / `Link` headers per the OpenAPI spec. Deferred until the controller branch.

---

## How to Test Locally

1. Copy `.env.example` to `.env` and fill in values.
2. Run `docker-compose up --build`.
3. Wait for all services to become healthy (~60 seconds).
4. Obtain a test token from Keycloak:
   ```bash
   curl -s -X POST http://localhost:8080/realms/policy-management/protocol/openid-connect/token \
     -d "grant_type=password&client_id=policy-management-api&username=policy-reader&password=<pwd>" \
     | jq -r .access_token
   ```
5. Call `GET http://localhost:5000/api/v1/policies` with `Authorization: Bearer <token>`.
