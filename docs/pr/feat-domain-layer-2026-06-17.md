# feat: implement Policy aggregate root, domain events, repository ports, and value objects

## Summary

This PR delivers the complete **Domain layer** for the Policy Management BFF — the innermost ring of the Clean Architecture onion, which has zero NuGet / framework dependencies. It introduces the `Policy` aggregate root with all 14 schema fields and full invariant enforcement via a static factory, the `FlagForReview()` behaviour method (idempotent, raises a domain event), the `LineOfBusiness` and `PolicyStatus` enumerations, repository and Unit-of-Work port interfaces (`IPolicyRepository`, `IUnitOfWork`), supporting value objects (`PagedResult<T>`, `PolicySearchCriteria`, `PolicySummaryData`), domain exception types (`DomainException`, `NotFoundException`), and the `PolicyFlaggedForReviewEvent`. This layer satisfies the domain modelling requirements of **FR-01 through FR-11** and **FR-14** as analysed in `docs/analysis/policy-management-bff-analysis.md`, and establishes the stable contracts against which all outer layers (Application, Infrastructure, API) will be built.

---

## Changes

### Domain

**`PolicyManagement.Domain`** — all new files, no framework or NuGet dependencies.

#### `Common/`
- **`AggregateRoot`** — abstract base class that owns an internal `List<IDomainEvent>`, exposes `DomainEvents` as a read-only list, and provides `RaiseDomainEvent()` / `ClearDomainEvents()` for aggregate-driven event publishing.
- **`IDomainEvent`** — marker interface with a single `OccurredAt : DateTime` property. All domain events implement this interface.
- **`PagedResult<T>`** — immutable `record` encapsulating a page of items, `TotalCount`, `Page`, `PageSize`, and derived boolean helpers `HasNextPage` / `HasPreviousPage` and computed `TotalPages` (ceiling division). Supports FR-01 / FR-02 pagination semantics.

#### `Entities/`
- **`Policy`** (sealed, aggregate root) — the sole aggregate root. Key decisions:
  - All 14 schema fields have `private set` accessors; external mutation is prohibited.
  - Static `Policy.Create(...)` factory enforces all domain invariants before construction:
    - `PolicyNumber` must match `^POL-\d{6}$` (compiled regex with 100 ms timeout — no ReDoS exposure).
    - `PremiumAmount` in the range [1,000 – 5,000,000] using `decimal` (never `float`).
    - `Currency` restricted to `{USD, SGD, HKD, AUD, JPY, THB}` via an `IReadOnlySet<string>`.
    - `Region` restricted to the eight APAC values via an `IReadOnlySet<string>` (ordinal-ignore-case).
    - `EffectiveDate` must be strictly before `ExpiryDate`.
    - Violations throw `DomainException` (maps to HTTP 422 by middleware).
  - `FlagForReview()` — idempotent: no-op if `FlaggedForReview` is already `true`; otherwise sets the flag and raises `PolicyFlaggedForReviewEvent` via `RaiseDomainEvent`. Supports FR-10.
  - Private parameterless constructor for EF Core materialisation.
  - `Id` is assigned via `Guid.NewGuid().ToString()` in the factory — the API consumer never sets it.

#### `Enumerations/`
- **`LineOfBusiness`** — `Property`, `Casualty`, `AccidentAndHealth`, `Marine`. The `AccidentAndHealth` member documents the `A&H` string stored in the database (value converter in Infrastructure). Supports FR-05.
- **`PolicyStatus`** — `Active`, `Expired`, `Pending`, `Cancelled`. Supports FR-04.

#### `Events/`
- **`PolicyFlaggedForReviewEvent`** — immutable `record` carrying `PolicyId` and `FlaggedAt`. Implements `IDomainEvent.OccurredAt` via `FlaggedAt`. Raised exactly once per policy, on first flag (idempotency in `FlagForReview()`). Supports FR-10.

#### `Exceptions/`
- **`DomainException`** — sealed; maps to HTTP 422 via `ExceptionHandlingMiddleware`.
- **`NotFoundException`** — sealed; message format `"{entityName} '{key}' was not found."`; maps to HTTP 404.

#### `Repositories/`
- **`IPolicyRepository`** — port interface (never an EF Core type) declaring:
  - `GetByIdAsync(string id)` → `Policy?` (FR-09)
  - `GetByIdsAsync(IEnumerable<string> ids)` → `IReadOnlyList<Policy>` (FR-10 bulk flag)
  - `SearchAsync(PolicySearchCriteria)` → `PagedResult<Policy>` (FR-01 to FR-08)
  - `GetSummaryAsync(DateOnly expiringSoonCutoff)` → `PolicySummaryData` (FR-11)
- **`IUnitOfWork`** — single `SaveChangesAsync()` method; Application handlers call this after all domain mutations to commit atomically.
- **`PolicySearchCriteria`** — immutable `record` encapsulating `Page`, `PageSize`, optional `Status`, `LineOfBusiness`, `Region`, `SearchTerm`, `SortBy`, `SortDescending`, `EffectiveDateFrom`, `EffectiveDateTo`. Directly maps to the `GET /api/v1/policies` query-string contract. Supports FR-02 through FR-08.
- **`PolicySummaryData`** — immutable `record` carrying total and per-status counts, a `IReadOnlyDictionary<(LineOfBusiness, Currency), decimal>` for multi-currency premium aggregation, and `ExpiringSoonCount`. Supports FR-11 and ADR-009.

### Application
No changes in this PR. Application CQRS handlers will depend on `IPolicyRepository` and `IUnitOfWork` as defined here.

### Infrastructure
No changes in this PR. EF Core `PolicyRepository` and `UnitOfWork` implementations will fulfil the ports defined here.

### API
No changes in this PR. Controllers and DTOs will be introduced in subsequent branches.

### Tests

**`PolicyManagement.Domain.Tests`** — 369 lines of new test code, xUnit + FluentAssertions.

- **`Entities/PolicyTests`**:
  - Factory happy path: valid input creates a `Policy` with all fields set correctly, `FlaggedForReview = false`, a non-empty UUID `Id`, and an empty `DomainEvents` list.
  - Invariant violations: `PolicyNumber` format (null, empty, wrong pattern), `PolicyholderName` blank, `PremiumAmount` below 1,000 and above 5,000,000, unsupported `Currency`, `effectiveDate >= expiryDate` (equal and after), unsupported `Region`.
  - `FlagForReview()`: first call sets `FlaggedForReview = true`, raises exactly one `PolicyFlaggedForReviewEvent` with correct `PolicyId` and `OccurredAt`; second call is a no-op (idempotency, no additional event).
  - `ClearDomainEvents()`: drains the event list, allowing the Infrastructure dispatcher to clear after publishing.
  - Boundary premium values (1,000 and 5,000,000) are accepted; 999.99 and 5,000,000.01 are rejected.

- **`Common/PagedResultTests`**:
  - `HasNextPage`: true when more items remain, false on last page, false when `TotalCount == PageSize`.
  - `HasPreviousPage`: false on page 1, true on page 2+.
  - `TotalPages`: parameterised theory covering ceiling division (10/3 → 4, 9/3 → 3, etc.), empty result (0 → 0), and guard on `PageSize == 0` (returns 0, no divide-by-zero).

### Infrastructure / DevOps
- **`.gitignore`** — added entries to exclude common IDE and build artefacts introduced during domain scaffolding.
- **`.github/agents/reviewer.agent.md`** — minor wording fix (1-line change).

### Documentation
No new documentation files in this PR. ADRs and architecture docs already capture the domain model decisions.

---

## Testing

**Unit tests** — `tests/PolicyManagement.Domain.Tests`:
- `PolicyTests` covers: factory happy path, all 9+ invariant violation scenarios, boundary premium values, `FlagForReview` first-call behaviour, `FlagForReview` idempotency, `ClearDomainEvents`.
- `PagedResultTests` covers: `HasNextPage` (3 cases), `HasPreviousPage` (2 cases), `TotalPages` (parameterised theory with 5 cases + zero-page-size guard).

**Integration tests** — None in this PR. The Domain layer has no infrastructure dependencies; integration tests will be introduced alongside Infrastructure and API implementations.

**Auth** — Not applicable at this layer. `TestAuthHandler` will be wired in the API integration test project in a later branch.

**How to run:**
```bash
dotnet test tests/PolicyManagement.Domain.Tests
# or all projects:
dotnet test
```

---

## Checklist

- [x] All changes derive from `docs/openapi/policy-management.yaml` domain model and `docs/analysis/policy-management-bff-analysis.md` FR/NFR analysis
- [x] Unit tests added for all new domain behaviour (`PolicyTests`, `PagedResultTests`)
- [ ] Integration tests — not applicable at Domain layer; deferred to API/Infrastructure branches
- [x] No hardcoded secrets or connection strings (Domain layer has no infrastructure concerns)
- [ ] `docker-compose up --build` — not yet verifiable; no runnable application in this branch
- [ ] Health checks — not yet applicable; no API project changes
- [x] No `Console.WriteLine` in production code; Domain layer has no logging concerns
- [ ] AI working journal updated (`docs/ai-working-journal.md`) — deferred
- [ ] Reviewer agent run — recommended before merge

---

## Risks

- **`A&H` enum storage**: `LineOfBusiness.AccidentAndHealth` requires an EF Core value converter to serialise as `"A&H"`. This converter is documented in the enum XML doc but must be implemented in `PolicyManagement.Infrastructure`; a missing or incorrect converter would silently corrupt data.
- **Domain event dispatch not yet wired**: `AggregateRoot.DomainEvents` are collected but no dispatcher exists yet. `PolicyFlaggedForReviewEvent` will be lost until Infrastructure publishes it post-`SaveChanges`. This is by design for this branch but must not be forgotten in the Infrastructure PR.
- **No `Add` / `Update` on `IPolicyRepository`**: The repository port is query-oriented; mutations flow exclusively through EF Core's change tracker and `IUnitOfWork.SaveChangesAsync`. This is intentional (ADR-002) but requires the Infrastructure team to ensure entities are tracked before mutation.

---

## Follow-up Work

1. **`feat/infrastructure-layer`** — Implement `PolicyRepository`, `UnitOfWork`, `PolicyDbContext`, `AuditSaveChangesInterceptor`, `LineOfBusiness` value converter, seed data (200+ realistic APAC policies), and EF Core migrations.
2. **`feat/application-layer`** — Implement MediatR CQRS handlers (`GetPoliciesQuery`, `GetPolicyByIdQuery`, `GetPolicySummaryQuery`, `BulkFlagPoliciesCommand`) with FluentValidation validators and pipeline behaviours (logging, validation).
3. **`feat/api-layer`** — Implement `PoliciesController`, DTOs/response records, `ExceptionHandlingMiddleware`, Keycloak JWT Bearer wiring, OpenAPI Swagger UI.
4. **Domain event publishing** — Wire `IDomainEvent` dispatch in `UnitOfWork.SaveChangesAsync` post-commit (optional Kafka bonus track).

---

## How to Test Locally

This branch contains only the Domain layer and its unit tests. No runnable application exists yet.

```bash
# Clone and restore
git clone <repo-url>
git checkout feat/domain-layer
dotnet restore

# Run domain unit tests
dotnet test tests/PolicyManagement.Domain.Tests --logger "console;verbosity=detailed"
```

Full end-to-end local testing will be available once the Infrastructure, Application, and API layers are implemented:

1. Copy `.env.example` to `.env` and fill in values
2. `docker-compose up --build`
3. Wait for all services healthy (~60 seconds)
4. Obtain a test token from Keycloak at `http://localhost:8080`
5. Call `GET http://localhost:5000/api/v1/policies`
