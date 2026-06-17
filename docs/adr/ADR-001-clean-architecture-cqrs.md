# ADR-001: Clean Architecture with CQRS via MediatR

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The Policy Management BFF must be maintainable, testable, and clearly layered so that infrastructure details (EF Core, SQL Server, Kafka) never bleed into business or orchestration logic. The domain logic for insurance policies — status transitions, flagging invariants, premium constraints — must be testable in isolation without a running database or HTTP stack.

The assessment explicitly requires demonstrating "clear architectural layering with dependencies pointing inward" and "properly separated API, service, domain, and infrastructure concerns". The team also requires consistent request validation and structured logging on every use-case without repeating that code in every handler.

---

## Decision

Adopt **Clean Architecture (Onion / Ports & Adapters)** with four .NET projects:

1. `PolicyManagement.Domain` — pure business core; zero external dependencies
2. `PolicyManagement.Application` — use-case orchestration via **MediatR CQRS**; knows only the Domain
3. `PolicyManagement.Infrastructure` — EF Core, SQL Server, repository implementations; knows Domain and Application interfaces
4. `PolicyManagement.Api` — ASP.NET Core BFF entry point; knows only Application

All dependencies point inward. The dependency rule is enforced structurally by project references: no project can reference a layer it must not depend on without an explicit `.csproj` change.

Use-cases are expressed as MediatR `IRequest<T>` commands (mutations) or queries (reads). Every handler is single-responsibility. A two-behaviour pipeline (`LoggingBehaviour` → `ValidationBehaviour` → Handler) provides uniform validation and timing without repetition.

---

## Consequences

### Positive

- Domain and Application layers are fully unit-testable without EF Core or ASP.NET Core
- Adding a new use-case means adding one folder with a command/query, handler, validator, and DTO — no existing file needs to change
- `ValidationBehaviour` ensures every command and query is validated before the handler runs; `400 Bad Request` handling is automatic
- `LoggingBehaviour` provides consistent structured timing logs for every request with zero per-handler boilerplate
- Layer boundaries are enforced at compile time by project reference rules

### Negative

- Four projects is more scaffolding than a flat architecture for a small service
- MediatR introduces indirection: finding the handler for a controller action requires knowing the request type, not following a direct call chain
- Pipeline behaviours add a small per-request overhead (negligible for a BFF workload)

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Vertical-slice / feature-folder monolith** (one project, use-case folders at the top level) | Does not enforce layer boundaries structurally; domain logic can quietly drift into infrastructure or controllers without a compile-time guard |
| **Plain service interfaces (`IPolicyService`)** instead of MediatR | Fat service classes accumulate mixed responsibilities over time; no built-in pipeline for cross-cutting concerns; switching from service-per-feature to CQRS later would require significant restructuring |
| **Microservices** (separate service per endpoint) | Severe over-engineering for a single bounded context with four endpoints; introduces distributed systems complexity (network failures, distributed tracing, deployment coordination) with no benefit at this scale |
| **Minimal API delegates** instead of controllers | Reduces scaffolding but provides no structural place for request-to-command mapping, contract types, or Swagger `[ProducesResponseType]` attributes; integration tests become harder to organise |
