---
applyTo: "src/PolicyManagement.Application/**/*.cs,src/PolicyManagement.Api/**/*.cs"
---

# CQRS and MediatR Standards — PolicyManagement BFF

## Overview

The Application layer implements CQRS (Command Query Responsibility Segregation) using MediatR as the in-process message bus. Every use-case is expressed as either a **Command** (mutates state) or a **Query** (reads state). Controllers dispatch requests via `ISender` and never contain business logic.

```
HTTP Request
    │
    ▼
Controller  ──── ISender.Send(command/query) ────►  MediatR Pipeline
                                                         │
                                              ┌──────────┴──────────┐
                                         ValidationBehaviour    LoggingBehaviour
                                              │
                                              ▼
                                         Handler
                                         │       │
                                    Domain    Repository
```

---

## Package Setup

```xml
<!-- PolicyManagement.Application.csproj -->
<PackageReference Include="MediatR" Version="12.*" />
<PackageReference Include="FluentValidation" Version="11.*" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.*" />
```

### Registration

```csharp
// Application/DependencyInjection.cs
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    var assembly = typeof(DependencyInjection).Assembly;

    services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(assembly);
        cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));      // outermost
        cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));   // innermost before handler
    });

    services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

    return services;
}
```

Pipeline behaviours are registered in order — `LoggingBehaviour` wraps `ValidationBehaviour` which wraps the handler.

---

## Command vs Query Separation Rules

| Dimension | Command | Query |
|---|---|---|
| **Intent** | Mutates state (create, update, delete, cancel) | Reads state only |
| **Return type** | `Task<TResponse>` where `TResponse` is a DTO or void | `Task<TResponse>` where `TResponse` is a DTO or list |
| **Side effects** | Allowed and expected (DB writes, events, emails) | **None** — must not mutate any state |
| **Idempotency** | Must be designed for idempotency where possible | Always idempotent by definition |
| **Naming** | `VerbNounCommand` | `GetNounQuery` / `ListNounsQuery` |
| **Marker interface** | `IRequest<TResponse>` (no special marker needed) | `IRequest<TResponse>` |

**Hard rule:** A query handler must never call `SaveChangesAsync`, `AddAsync`, `UpdateAsync`, or `DeleteAsync` on a repository. If you need to return data *and* persist something (e.g., audit a read), use a separate command dispatched from the controller after the query.

---

## Naming Conventions

### Commands

```
<Verb><Noun>Command          CreatePolicyCommand
                             CancelPolicyCommand
                             UpdateClaimStatusCommand
                             DeleteCustomerCommand
```

### Queries

```
Get<Noun>ByIdQuery           GetPolicyByIdQuery
List<Nouns>Query             ListPoliciesQuery
Get<Noun><Filter>Query       GetPoliciesByCustomerIdQuery
```

### Handlers

```
<CommandOrQueryName>Handler  CreatePolicyCommandHandler
                             GetPolicyByIdQueryHandler
```

### Response DTOs

```
<Noun>Dto                    PolicyDto          (used as handler return type)
<Noun>Response               PolicyResponse     (API contract — separate type in Api layer)
<Noun>Summary                PolicySummary      (list item DTO — less detail than full Dto)
```

### Validators

```
<CommandOrQueryName>Validator   CreatePolicyCommandValidator
                                ListPoliciesQueryValidator
```

### Folder Structure

```
Application/
└── Policies/
    ├── Commands/
    │   ├── CreatePolicy/
    │   │   ├── CreatePolicyCommand.cs
    │   │   ├── CreatePolicyCommandHandler.cs
    │   │   └── CreatePolicyCommandValidator.cs
    │   └── CancelPolicy/
    │       ├── CancelPolicyCommand.cs
    │       ├── CancelPolicyCommandHandler.cs
    │       └── CancelPolicyCommandValidator.cs
    └── Queries/
        ├── GetPolicyById/
        │   ├── GetPolicyByIdQuery.cs
        │   ├── GetPolicyByIdQueryHandler.cs
        │   └── PolicyDto.cs
        └── ListPolicies/
            ├── ListPoliciesQuery.cs
            ├── ListPoliciesQueryHandler.cs
            └── PolicySummary.cs
```

Each use-case lives in its own sub-folder. The command/query, handler, validator, and response DTO are co-located — no hunting across the codebase.

---

## Handler Conventions

### Command Handler

```csharp
// Application/Policies/Commands/CreatePolicy/CreatePolicyCommand.cs
public sealed record CreatePolicyCommand(
    string CustomerId,
    string ProductCode,
    DateOnly EffectiveDate,
    decimal CoverageAmount
) : IRequest<PolicyDto>;

// Application/Policies/Commands/CreatePolicy/CreatePolicyCommandHandler.cs
internal sealed class CreatePolicyCommandHandler(
    ICustomerRepository customerRepository,
    IPolicyRepository policyRepository,
    IUnitOfWork unitOfWork,
    ILogger<CreatePolicyCommandHandler> logger
) : IRequestHandler<CreatePolicyCommand, PolicyDto>
{
    public async Task<PolicyDto> Handle(CreatePolicyCommand command, CancellationToken ct)
    {
        // 1. Fetch dependencies / guard
        var customer = await customerRepository.GetByIdAsync(command.CustomerId, ct)
            ?? throw new NotFoundException(nameof(Customer), command.CustomerId);

        // 2. Delegate creation to the domain
        var policy = Policy.Create(
            command.CustomerId,
            command.ProductCode,
            command.EffectiveDate,
            command.CoverageAmount);

        // 3. Persist
        await policyRepository.AddAsync(policy, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Policy {PolicyId} created for customer {CustomerId}",
            policy.Id, command.CustomerId);

        // 4. Return DTO — never return a domain entity
        return policy.ToDto();
    }
}
```

### Query Handler

```csharp
// Application/Policies/Queries/GetPolicyById/GetPolicyByIdQuery.cs
public sealed record GetPolicyByIdQuery(string PolicyId) : IRequest<PolicyDto>;

// Application/Policies/Queries/GetPolicyById/GetPolicyByIdQueryHandler.cs
internal sealed class GetPolicyByIdQueryHandler(
    IPolicyRepository policyRepository
) : IRequestHandler<GetPolicyByIdQuery, PolicyDto>
{
    public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
    {
        var policy = await policyRepository.GetByIdAsync(query.PolicyId, ct)
            ?? throw new NotFoundException(nameof(Policy), query.PolicyId);

        return policy.ToDto();
    }
}
```

### Handler Rules

- Handlers are `internal sealed` — they are implementation details of the Application layer, not public API
- Commands and queries are `sealed record` types — immutable, value-comparable, and serialisation-friendly
- Handlers receive dependencies via **primary constructor** (C# 12)
- A handler must do exactly one thing: orchestrate the use-case. No branching unrelated to the single responsibility
- Handlers never return domain entities — always map to a DTO before returning
- `CancellationToken` is always the last parameter and is always forwarded to every async call

---

## What Belongs Where

### Handler Responsibilities (do / do not)

| ✅ Handler does | ❌ Handler does NOT do |
|---|---|
| Fetch aggregates via repository | Contain business rule logic |
| Call domain methods to mutate state | Access `DbContext` directly |
| Persist changes via `IUnitOfWork` | Know about HTTP or `IActionResult` |
| Map domain entities to DTOs | Contain `if/else` business decisions |
| Dispatch domain events (if not auto-dispatched) | Reference `Microsoft.AspNetCore.*` namespaces |
| Log business events | Validate input (delegated to pipeline) |

### Domain Responsibilities

Business rules and invariants that belong in the **domain entity**, not the handler:

```csharp
// ✅ RIGHT — invariant enforced on the entity
public class Policy
{
    public void Cancel(string reason)
    {
        if (Status == PolicyStatus.Cancelled)
            throw new DomainException("Policy is already cancelled.");

        Status = PolicyStatus.Cancelled;
        CancellationReason = reason;
        AddDomainEvent(new PolicyCancelledEvent(Id, reason));
    }
}

// ❌ WRONG — business rule leaks into the handler
public async Task<Unit> Handle(CancelPolicyCommand command, CancellationToken ct)
{
    var policy = await _repo.GetByIdAsync(command.PolicyId, ct);
    if (policy.Status == PolicyStatus.Cancelled)   // ← business logic in handler
        throw new DomainException("Already cancelled");
    policy.Status = PolicyStatus.Cancelled;         // ← direct property mutation
    ...
}
```

### Repository Responsibilities

Repositories abstract persistence. Handlers call repository methods; they never use LINQ or `DbContext` directly.

```csharp
// ✅ RIGHT — handler calls a named repository method
var policies = await _policyRepository.GetByCustomerIdAsync(customerId, page, pageSize, ct);

// ❌ WRONG — handler queries the DbContext directly
var policies = await _dbContext.Policies
    .Where(p => p.CustomerId == customerId)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync(ct);
```

---

## Pipeline Behaviours

### ValidationBehaviour

Runs FluentValidation automatically before every handler. Throws `ValidationException` (FluentValidation) if any validator for the request type finds errors.

```csharp
// Application/Common/Behaviours/ValidationBehaviour.cs
public sealed class ValidationBehaviour<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

The global `ExceptionHandlingMiddleware` catches `ValidationException` and maps it to `400 Bad Request` with the `errors` field map — handlers never catch `ValidationException`.

### LoggingBehaviour

Wraps every handler with structured request/response logging. Registered as the outermost behaviour.

```csharp
// Application/Common/Behaviours/LoggingBehaviour.cs
public sealed class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation("Handling {RequestName}", requestName);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();

            logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Request {RequestName} failed after {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
            throw;  // always rethrow — behaviours must not swallow exceptions
        }
    }
}
```

### Pipeline Execution Order

```
LoggingBehaviour (start)
  └─► ValidationBehaviour (start)
        └─► Handler.Handle()
      ValidationBehaviour (end)  ← throws ValidationException if invalid
LoggingBehaviour (end)           ← logs timing; rethrows on exception
```

---

## FluentValidation Integration

### Validator Conventions

```csharp
// Application/Policies/Commands/CreatePolicy/CreatePolicyCommandValidator.cs
internal sealed class CreatePolicyCommandValidator : AbstractValidator<CreatePolicyCommand>
{
    public CreatePolicyCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .MaximumLength(20)
            .Matches(@"^[A-Z]{2,20}$")
            .WithMessage("Product code must contain only uppercase letters.");

        RuleFor(x => x.EffectiveDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Effective date must be today or in the future.");

        RuleFor(x => x.CoverageAmount)
            .GreaterThan(0)
            .WithMessage("Coverage amount must be greater than zero.");
    }
}
```

### Validator Rules

- Validators are `internal sealed` — co-located with their command/query
- Auto-registered via `services.AddValidatorsFromAssembly(...)` — no manual registration
- One validator per command/query type — no shared base validators across unrelated types
- Use `.WithMessage(...)` on every rule that has a non-obvious error message
- Validators check **structural and format constraints** only — domain invariants belong in the domain entity
- Never inject repositories or `DbContext` into validators to perform existence checks — that is the handler's job

### Distinguishing Validator vs Domain Invariants

| Rule | Where it lives | Reason |
|---|---|---|
| `CustomerId` must not be empty | Validator | Structural — no domain knowledge needed |
| `EffectiveDate` format is valid | Validator | Format check |
| `CoverageAmount > 0` | Validator | Range constraint |
| Policy cannot be cancelled if already cancelled | Domain entity | Business state transition rule |
| A customer must exist before creating a policy | Handler (NotFoundException guard) | Requires repository lookup |
| Premium calculation based on coverage amount | Domain service | Complex business logic |

---

## Controller → MediatR Dispatch

Controllers inject only `ISender` (not `IMediator`). They translate HTTP requests to commands/queries and HTTP responses from DTOs.

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/policies")]
public class PoliciesController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePolicyRequest request,
        CancellationToken ct)
    {
        var command = new CreatePolicyCommand(
            request.CustomerId,
            request.ProductCode,
            request.EffectiveDate,
            request.CoverageAmount);

        var dto = await sender.Send(command, ct);

        return CreatedAtAction(nameof(GetById), new { policyId = dto.Id }, dto.ToResponse());
    }

    [HttpGet("{policyId}")]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string policyId, CancellationToken ct)
    {
        var dto = await sender.Send(new GetPolicyByIdQuery(policyId), ct);
        return Ok(dto.ToResponse());
    }
}
```

**Controller rules:**
- Inject `ISender`, not `IMediator` — controllers only need to send requests, not publish notifications
- Map `Request → Command/Query` in the controller action; do not pass API request types into the Application layer
- Map `DTO → Response` in the controller action; do not expose Application DTOs as HTTP response bodies
- Never add `try/catch` in controllers — exception handling is the middleware's responsibility

---

## Anti-Patterns to Avoid

### ❌ Fat Handler (business logic in handler)

```csharp
// WRONG — policy cancellation logic belongs in the domain entity
public async Task<Unit> Handle(CancelPolicyCommand cmd, CancellationToken ct)
{
    var policy = await _repo.GetByIdAsync(cmd.PolicyId, ct);
    if (policy.Status == PolicyStatus.Cancelled)
        throw new DomainException("Already cancelled.");
    if (policy.Status == PolicyStatus.Expired)
        throw new DomainException("Cannot cancel expired policy.");
    policy.Status = PolicyStatus.Cancelled;       // direct setter
    policy.CancelledAt = DateTime.UtcNow;
    await _unitOfWork.SaveChangesAsync(ct);
}
```

**Fix:** Move all state transition logic into `Policy.Cancel(reason)`.

---

### ❌ Querying DbContext Directly in Handler

```csharp
// WRONG — handler bypasses the repository abstraction
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
{
    return await _dbContext.Policies
        .Where(p => p.Id == query.PolicyId)
        .Select(p => new PolicyDto(...))
        .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(...);
}
```

**Fix:** Add a `GetByIdAsync` method to `IPolicyRepository` and call it.

---

### ❌ Returning Domain Entity from Handler

```csharp
// WRONG — leaks domain type across layer boundary
public sealed record GetPolicyByIdQuery(string PolicyId) : IRequest<Policy>;
```

**Fix:** Return `PolicyDto`. Map inside the handler using a `ToDto()` extension method.

---

### ❌ Injecting IMediator into Another Handler

```csharp
// WRONG — chains handlers via MediatR inside Application logic
public async Task<PolicyDto> Handle(CreatePolicyCommand cmd, CancellationToken ct)
{
    await _mediator.Send(new ValidateCustomerCommand(cmd.CustomerId), ct); // ← hidden coupling
    ...
}
```

**Fix:** Call the repository or domain service directly. Use MediatR only from controllers and integration event handlers — never from within another handler.

---

### ❌ Putting Validation Logic Inside the Handler

```csharp
// WRONG — handler re-validates what the pipeline should catch
public async Task<PolicyDto> Handle(CreatePolicyCommand cmd, CancellationToken ct)
{
    if (string.IsNullOrEmpty(cmd.CustomerId))     // ← pipeline already does this
        throw new ValidationException("CustomerId is required.");
    ...
}
```

**Fix:** Let `ValidationBehaviour` call the registered `IValidator<CreatePolicyCommand>` before the handler runs.

---

### ❌ Command That Only Reads (Query Disguised as Command)

```csharp
// WRONG — named as a command but returns data without mutation
public sealed record GetActivePoliciesCommand : IRequest<List<PolicyDto>>;
```

**Fix:** Rename to `ListActivePoliciesQuery` and ensure the handler never writes to any repository.

---

### ❌ Shared/Generic Handler for Multiple Use-Cases

```csharp
// WRONG — one handler tries to serve both create and update
public class SavePolicyCommandHandler : IRequestHandler<SavePolicyCommand, PolicyDto>
{
    public async Task<PolicyDto> Handle(SavePolicyCommand cmd, CancellationToken ct)
    {
        if (cmd.PolicyId is null) { /* create */ } else { /* update */ }
    }
}
```

**Fix:** Separate `CreatePolicyCommand` and `UpdatePolicyCommand` with dedicated handlers. Each handler has one purpose.

---

## Enforcement Checklist

Before raising a PR that adds or modifies a command, query, or handler:

- [ ] Commands are named `VerbNounCommand`; queries are named `GetNounQuery` or `ListNounsQuery`
- [ ] Command/query and handler are `sealed` and co-located in a dedicated sub-folder
- [ ] Handlers are `internal` — not `public`
- [ ] Handler returns a DTO, not a domain entity
- [ ] Query handler does not call `SaveChangesAsync`, `AddAsync`, or any write method
- [ ] Business rules and state transitions are in domain entities, not handlers
- [ ] `DbContext` is not injected or referenced in any handler
- [ ] `ValidationBehaviour` is registered — handler does not manually invoke validators
- [ ] Controller injects `ISender`, not `IMediator`
- [ ] Controller maps API request → command/query and DTO → API response; no business logic in controller
