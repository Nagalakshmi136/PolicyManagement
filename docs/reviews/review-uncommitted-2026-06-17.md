# Review: Uncommitted Changes — feat/domain-layer
**Date:** 2026-06-17  
**Branch:** feat/domain-layer  
**Scope:** All untracked files in `src/PolicyManagement.Domain/`

---

## Architecture ✅

The Domain layer is structurally correct. All classes sit in the right namespaces
(`Common`, `Entities`, `Enumerations`, `Events`, `Exceptions`, `Repositories`).
The `Policy` entity inherits from `AggregateRoot`, raises domain events through
the protected `RaiseDomainEvent` helper, and exposes only private setters —
direct property mutation from outside the class is not possible.

~~**One gap:** `IPolicyRepository`'s own XML doc says "Mutations are persisted
through `IUnitOfWork` in the Application layer", but no `IUnitOfWork` interface
existed. Handlers that need to commit a transaction had nowhere to go.~~  
**Resolved:** `IUnitOfWork` added to `src/PolicyManagement.Domain/Repositories/IUnitOfWork.cs`
with `Task<int> SaveChangesAsync(CancellationToken)`.

~~**Second gap:** `Policy` had no public factory method or public constructor. EF
Core could materialise existing rows but no layer could ever create a new `Policy`
aggregate.~~  
**Resolved:** `public static Policy Create(...)` factory added to `Policy.cs`.
Enforces all invariants: `PolicyNumber` format (`^POL-\d{6}$`), `PremiumAmount`
range [1,000–5,000,000], `EffectiveDate < ExpiryDate`, currency whitelist, region
whitelist. Throws `DomainException` on violation.

## Security ✅

No security concerns at the domain layer. The domain has zero framework dependencies
(`PolicyManagement.Domain.csproj` has no `<PackageReference>` entries), so there
is no surface for injection, authentication bypass, or secret leakage here.

## Performance ✅

~~`IPolicyRepository.GetAllAsync()` was defined to return the **full unfiltered
policy list** for in-memory summary aggregation — a known O(n) anti-pattern.~~  
**Resolved:** `GetAllAsync` removed. Replaced with `GetSummaryAsync(DateOnly expiringSoonCutoff,
CancellationToken)` returning a `PolicySummaryData` aggregate DTO. All counts and
premium totals are now computed server-side in SQL. The `expiringSoonCutoff` date
is passed as a parameter so the repository can apply a `WHERE ExpiryDate BETWEEN
@today AND @cutoff` predicate (driven by `Cache:ExpiringSoonDays`, default 30).

## Test Coverage ✅

~~`tests/PolicyManagement.Domain.Tests/` contained only generated obj files — zero
test classes existed.~~  
**Resolved:** 45 tests added, all passing.

- **`Entities/PolicyTests`** — factory happy path; all invariant violation scenarios
  (PolicyNumber format, PremiumAmount range with boundaries, Currency whitelist,
  effectiveDate >= expiryDate, Region whitelist); `FlagForReview()` first-call
  (sets flag, raises exactly one `PolicyFlaggedForReviewEvent` with correct payload);
  `FlagForReview()` second call is a no-op (no additional event raised).
- **`Common/PagedResultTests`** — `HasNextPage` (3 cases), `HasPreviousPage` (2 cases),
  `TotalPages` parameterised theory (5 cases + zero-page-size guard).

```
Passed! Failed: 0, Passed: 45, Skipped: 0, Total: 45
```

## Production Readiness ✅

No production-readiness concerns at the domain layer. There are no `Console.WriteLine`
calls, no hardcoded config, and no logging concerns (domain objects correctly have
no logging dependency).

## Contract Compliance ✅

~~**`PolicySearchCriteria` was missing two filter parameters** defined in
`docs/openapi/policy-management.yaml`: `effectiveDateFrom` and `effectiveDateTo`.~~  
**Resolved:** `DateOnly? EffectiveDateFrom = null` and `DateOnly? EffectiveDateTo = null`
added to `PolicySearchCriteria.cs`.

**`LineOfBusiness.AccidentAndHealth` ↔ `"A&H"` serialization:** The domain correctly
names the member `AccidentAndHealth` and documents the storage alias. A custom
`JsonStringEnumConverter` or `EnumMemberAttribute` mapping must exist at the API
layer (not the domain) before the list endpoint will round-trip correctly.
**Deferred to Infrastructure/API layer review** — flagged as a known risk in the PR description.

---

## Verdict

**Approved** ✅ — all required changes addressed. Remaining `A&H` wire-format
mapping is tracked as a known risk in the PR and must be verified in the
Infrastructure/API implementation review.

---

## Required Actions — Resolution Status

| # | File | Issue | Status |
|---|---|---|---|
| 1 | `src/PolicyManagement.Domain/Repositories/IPolicyRepository.cs` | Replace `GetAllAsync` with `GetSummaryAsync(DateOnly expiringSoonCutoff, CancellationToken)` returning `PolicySummaryData` | ✅ Done |
| 2 | `src/PolicyManagement.Domain/Repositories/IUnitOfWork.cs` | Define `IUnitOfWork` with `Task<int> SaveChangesAsync(CancellationToken)` | ✅ Done |
| 3 | `src/PolicyManagement.Domain/Entities/Policy.cs` | Add `Policy.Create(...)` static factory with full invariant enforcement | ✅ Done |
| 4 | `src/PolicyManagement.Domain/Repositories/PolicySearchCriteria.cs` | Add `DateOnly? EffectiveDateFrom` and `DateOnly? EffectiveDateTo` | ✅ Done |
| 5 | `tests/PolicyManagement.Domain.Tests/` | Unit tests for `Policy.FlagForReview()` and `PagedResult<T>` computed properties | ✅ Done (45 tests) |