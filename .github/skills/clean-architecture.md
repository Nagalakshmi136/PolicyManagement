---
applyTo: "src/**/*.cs,tests/**/*.cs"
---

# Clean Architecture Standards — PolicyManagement BFF

## Overview

This project follows Clean Architecture (Onion / Ports & Adapters) with four layers. The cardinal rule is the **Dependency Rule**: source-code dependencies must point **inward only** — outer layers depend on inner layers, never the reverse.

```
┌─────────────────────────────────────────┐
│               API Layer                 │  ← outermost
│  ┌───────────────────────────────────┐  │
│  │        Application Layer          │  │
│  │  ┌─────────────────────────────┐  │  │
│  │  │      Domain Layer           │  │  │  ← innermost (no deps)
│  │  └─────────────────────────────┘  │  │
│  └───────────────────────────────────┘  │
│         Infrastructure Layer            │  ← outer, plugs into ports
└─────────────────────────────────────────┘
```

Allowed project references:
```
PolicyManagement.Api            → PolicyManagement.Application
PolicyManagement.Application    → PolicyManagement.Domain
PolicyManagement.Infrastructure → PolicyManagement.Domain
```
No other cross-project references are permitted.

---

## Layer Definitions and Responsibilities

### 1. Domain — `PolicyManagement.Domain`

The pure business core. Has **zero** dependencies on any framework, ORM, or external library.

**Responsibilities:**
- Define aggregate roots, entities, and value objects (e.g., `Policy`, `Claim`, `Customer`, `PolicyNumber`, `CoverageAmount`)
- Encode invariants and business rules directly on the model (rich domain, not anemic)
- Define domain events (e.g., `PolicyIssuedEvent`, `ClaimFiledEvent`)
- Define repository interfaces (ports) that Infrastructure will implement
- Define domain exceptions (`DomainException`, `PolicyNotFoundException`, etc.)

**Allowed dependencies:** none (no NuGet packages except optional primitive helpers with no transitive deps)

### 2. Application — `PolicyManagement.Application`

Orchestrates use-cases. Knows the domain; knows nothing about HTTP, EF Core, or SQL.

**Responsibilities:**
- Implement use-case handlers (CQRS commands and queries via MediatR, or plain service classes)
- Define application-level interfaces (ports) for external concerns: `IEmailService`, `IPolicyRepository`, `IUnitOfWork`
- Define DTOs / request+response models used by use-case boundaries
- Implement cross-cutting concerns scoped to use-cases: validation (FluentValidation), mapping (manual or Mapster)
- Return `Result<T>` or throw application exceptions; never return HTTP types

**Allowed dependencies:** `PolicyManagement.Domain`, MediatR, FluentValidation, Mapster (mapping only)

### 3. Infrastructure — `PolicyManagement.Infrastructure`

Implements the ports defined by Application and Domain. Bridges the system to the outside world.

**Responsibilities:**
- Implement repository interfaces using EF Core (`PolicyRepository : IPolicyRepository`)
- Define `AppDbContext`, EF Core entity configurations (`IEntityTypeConfiguration<T>`), and migrations
- Implement external service adapters (HTTP clients, email, blob storage)
- Register all infrastructure services via `IServiceCollection` extension method (`AddInfrastructure`)

**Allowed dependencies:** `PolicyManagement.Domain`, `PolicyManagement.Application` (for interface contracts only), EF Core, SQL Server provider, Polly, HttpClientFactory

**Never:** expose `DbContext` or EF Core types beyond this layer

### 4. API — `PolicyManagement.Api`

The BFF entry point. Thin layer that translates HTTP ↔ application use-cases.

**Responsibilities:**
- Define controllers or minimal-API endpoints (route, HTTP verb, auth policy)
- Map HTTP request models → application commands/queries; map results → HTTP responses
- Implement global exception handling middleware (translate domain/application exceptions to `ProblemDetails`)
- Configure DI container by calling `AddApplication()` and `AddInfrastructure()` from `Program.cs`
- Define API-level request/response contracts (separate from application DTOs where the shapes differ)
- Apply BFF shaping: aggregate multiple application calls into a single client-optimised response when needed

**Allowed dependencies:** `PolicyManagement.Application`, MediatR (dispatch only), ASP.NET Core, Swashbuckle/Scalar

**Never:** reference `PolicyManagement.Infrastructure` directly (only via DI registration); never reference Domain entities in controllers

---

## Folder Structure Conventions

```
src/
├── PolicyManagement.Domain/
│   ├── Entities/
│   │   ├── Policy.cs
│   │   ├── Claim.cs
│   │   └── Customer.cs
│   ├── ValueObjects/
│   │   ├── PolicyNumber.cs
│   │   └── CoverageAmount.cs
│   ├── Events/
│   │   └── PolicyIssuedEvent.cs
│   ├── Repositories/           ← interfaces (ports)
│   │   └── IPolicyRepository.cs
│   └── Exceptions/
│       └── PolicyNotFoundException.cs
│
├── PolicyManagement.Application/
│   ├── Policies/               ← feature slice
│   │   ├── Commands/
│   │   │   ├── CreatePolicy/
│   │   │   │   ├── CreatePolicyCommand.cs
│   │   │   │   ├── CreatePolicyCommandHandler.cs
│   │   │   │   └── CreatePolicyCommandValidator.cs
│   │   │   └── CancelPolicy/
│   │   ├── Queries/
│   │   │   └── GetPolicyById/
│   │   │       ├── GetPolicyByIdQuery.cs
│   │   │       ├── GetPolicyByIdQueryHandler.cs
│   │   │       └── PolicyDto.cs
│   ├── Claims/                 ← feature slice
│   ├── Common/
│   │   ├── Interfaces/         ← application-level ports
│   │   │   └── IUnitOfWork.cs
│   │   └── Behaviours/         ← MediatR pipeline behaviours
│   │       └── ValidationBehaviour.cs
│   └── DependencyInjection.cs  ← AddApplication()
│
├── PolicyManagement.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/
│   │   │   └── PolicyConfiguration.cs
│   │   ├── Repositories/
│   │   │   └── PolicyRepository.cs
│   │   └── Migrations/
│   ├── ExternalServices/
│   │   └── EmailService.cs
│   └── DependencyInjection.cs  ← AddInfrastructure()
│
└── PolicyManagement.Api/
    ├── Controllers/            ← or Endpoints/ for minimal API
    │   └── PoliciesController.cs
    ├── Middleware/
    │   └── ExceptionHandlingMiddleware.cs
    ├── Contracts/              ← API-specific request/response models
    │   └── CreatePolicyRequest.cs
    └── Program.cs

tests/
├── PolicyManagement.Domain.Tests/
├── PolicyManagement.Application.Tests/
│   └── Policies/
│       └── CreatePolicyCommandHandlerTests.cs
├── PolicyManagement.Infrastructure.Tests/
└── PolicyManagement.Api.Tests/         ← integration tests
```

---

## Dependency Rules — Quick Reference

| From (project) | May reference | Must NOT reference |
|---|---|---|
| `Domain` | _(nothing)_ | Application, Infrastructure, Api |
| `Application` | Domain | Infrastructure, Api |
| `Infrastructure` | Domain, Application | Api |
| `Api` | Application | Infrastructure directly*, Domain entities in controllers |

\* Infrastructure is registered into the DI container from `Program.cs` — this one reference is acceptable. Controllers must not `new` up infrastructure types or hold `DbContext` references.

---

## What Belongs in Each Layer — Decision Guide

| Concern | Layer | Notes |
|---|---|---|
| Business invariant ("a policy must have a valid effective date") | Domain | Enforce in entity constructor or factory method |
| Use-case orchestration ("create a policy and send a confirmation email") | Application | CommandHandler composes domain + port calls |
| EF Core `DbContext` | Infrastructure | Never expose beyond Infrastructure |
| Repository interface | Domain or Application | Domain if needed by domain services; Application if orchestration-only |
| FluentValidation rules for a command | Application | Co-locate with the command in its feature slice folder |
| `ProblemDetails` error shaping | Api | Global middleware; never in Application |
| HTTP status code decisions | Api | Application returns `Result<T>` or throws typed exceptions |
| Database migration | Infrastructure | `dotnet ef migrations add` run from Infrastructure project |
| JWT / Auth policy configuration | Api | `Program.cs` or a dedicated `AuthExtensions.cs` |
| Pagination helpers | Application (common) | Domain must not know about page size |

---

## Common Violations and How to Avoid Them

### ❌ Domain references EF Core
```csharp
// WRONG — Policy entity inherits EF marker or uses [Key] attribute
using Microsoft.EntityFrameworkCore;
public class Policy : IEntity { ... }
```
**Fix:** Remove all EF Core references from Domain. Apply EF configuration in Infrastructure using `IEntityTypeConfiguration<Policy>`.

---

### ❌ Application layer returns `IActionResult` / HTTP types
```csharp
// WRONG — handler knows about HTTP
public async Task<IActionResult> Handle(CreatePolicyCommand cmd, ...)
```
**Fix:** Return a domain object, DTO, or `Result<PolicyDto>`. Let the controller map to `IActionResult`.

---

### ❌ Controller directly instantiates a repository or DbContext
```csharp
// WRONG
public PoliciesController(AppDbContext db) { _db = db; }
```
**Fix:** Inject the MediatR `ISender` (or an application service interface). The controller dispatches commands/queries and never touches persistence directly.

---

### ❌ Infrastructure type leaks into Application
```csharp
// WRONG — Application handler takes a concrete EF type
public GetPolicyQueryHandler(AppDbContext db) { ... }
```
**Fix:** Define `IPolicyRepository` in Domain or Application; inject the interface. Infrastructure registers the concrete `PolicyRepository` against that interface in `AddInfrastructure()`.

---

### ❌ Domain logic scattered into Application handlers
```csharp
// WRONG — business rule lives in handler, not entity
if (command.EffectiveDate < DateTime.UtcNow)
    throw new ValidationException("Effective date must be in the future");
```
**Fix:** Encode invariants in the domain entity or a domain service. The handler calls `Policy.Create(...)` which enforces the rule internally and throws `DomainException` on violation.

---

### ❌ Circular or skip-layer references
```csharp
// WRONG — Domain references Application
using PolicyManagement.Application.Policies.Commands;
```
**Fix:** Only inner layers define contracts. Outer layers implement or consume them. Run `dotnet build` with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — circular project references are a build error.

---

### ❌ Anemic domain model
```csharp
// WRONG — Policy is a bag of properties; logic lives elsewhere
public class Policy { public PolicyStatus Status { get; set; } }
// ...and in some service:
policy.Status = PolicyStatus.Cancelled;
```
**Fix:** Move state-changing behaviour onto the entity:
```csharp
public class Policy
{
    public PolicyStatus Status { get; private set; }

    public void Cancel(string reason)
    {
        if (Status == PolicyStatus.Cancelled)
            throw new DomainException("Policy is already cancelled.");
        Status = PolicyStatus.Cancelled;
        AddDomainEvent(new PolicyCancelledEvent(Id, reason));
    }
}
```

---

## Enforcement Checklist

Before raising a PR, verify:

- [ ] No `using` directives in Domain that reference Application, Infrastructure, or ASP.NET Core namespaces
- [ ] No `using` directives in Application that reference Infrastructure or ASP.NET Core namespaces
- [ ] Controllers only inject `ISender` (MediatR) or explicitly-defined application service interfaces
- [ ] `AppDbContext` is internal to Infrastructure (or at minimum, not injected outside of it)
- [ ] Repository interfaces live in Domain or Application, not Infrastructure
- [ ] All domain state changes go through entity methods, not property setters
- [ ] `DependencyInjection.cs` extension methods (`AddApplication`, `AddInfrastructure`) are the only registration entry points called from `Program.cs`
- [ ] Feature slices in Application are self-contained folders: command/query + handler + validator + DTO together
