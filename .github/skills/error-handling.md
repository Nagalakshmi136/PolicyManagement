---
applyTo: "src/**/*.cs"
---

# Error Handling Standards — PolicyManagement BFF

## Core Principle

All error handling is centralised in `ExceptionHandlingMiddleware`. Controllers and handlers **never** contain `try/catch` blocks for expected error conditions. Every error response uses RFC 9457 **ProblemDetails** (`application/problem+json`) — no custom envelopes, no plain strings.

```
Request
  │
  ▼
ExceptionHandlingMiddleware   ← catches all unhandled exceptions
  │
  ▼
CorrelationIdMiddleware
  │
  ▼
MediatR Pipeline
  ├── ValidationBehaviour     ← throws ValidationException (→ 400)
  └── Handler
        ├── throws NotFoundException      → 404
        ├── throws ConflictException      → 409
        ├── throws DomainException        → 422
        └── throws anything else          → 500
```

---

## Exception Hierarchy

Define all custom exceptions in `PolicyManagement.Application/Common/Exceptions/` (or `Domain/Exceptions/` for domain-specific ones).

```csharp
// Application/Common/Exceptions/NotFoundException.cs
public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} with identifier '{key}' was not found.");

// Application/Common/Exceptions/ConflictException.cs
public sealed class ConflictException(string message) : Exception(message);

// Domain/Exceptions/DomainException.cs
public sealed class DomainException(string message) : Exception(message);

// FluentValidation.ValidationException is used as-is — no wrapper needed
```

---

## Standard Error Response Shape

All error responses conform to RFC 9457 (`application/problem+json`).

### Base ProblemDetails (all non-validation errors)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Policy with identifier 'POL-999' was not found.",
  "instance": "/api/v1/policies/POL-999",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

### Validation ProblemDetails (400 only)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/policies",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "customerId": ["'Customer Id' must not be empty."],
    "coverageAmount": ["Coverage amount must be greater than zero."],
    "effectiveDate": ["Effective date must be today or in the future."]
  }
}
```

### Field Rules

| Field | Required on | Value |
|---|---|---|
| `type` | All errors | RFC 9110 URI for the status code |
| `title` | All errors | Standard HTTP reason phrase — never a custom message |
| `status` | All errors | Numeric HTTP status code (mirrors response status) |
| `detail` | All except 500 | Human-readable explanation — omitted on 500 |
| `instance` | All errors | Request path (e.g., `/api/v1/policies/POL-001`) |
| `traceId` | All errors | `Activity.Current?.Id` or `HttpContext.TraceIdentifier` |
| `errors` | 400 only | Field-keyed validation error map |

The `errors` map must use `camelCase` field names matching the API request contract. Never use the C# property name (PascalCase).

---

## HTTP Status Code Usage Rules

| Status | When to use | Exception type |
|---|---|---|
| `400 Bad Request` | Input fails structural or format validation (missing fields, wrong type, constraint violation) | `ValidationException` (FluentValidation) |
| `401 Unauthorized` | Request lacks valid authentication credentials | ASP.NET Core auth middleware |
| `403 Forbidden` | Authenticated but not authorised for the action | `UnauthorizedAccessException` or auth policy |
| `404 Not Found` | Requested resource does not exist | `NotFoundException` |
| `409 Conflict` | Request conflicts with current resource state (duplicate key, concurrent edit) | `ConflictException` |
| `422 Unprocessable Entity` | Request is structurally valid but violates a business rule | `DomainException` |
| `500 Internal Server Error` | Unexpected fault not covered by the above | Any unhandled `Exception` |

### 400 vs 422 — The Critical Distinction

```
400 Bad Request       → Input is malformed or structurally invalid.
                        "The field coverageAmount is required."
                        "effectiveDate must be a valid date."

422 Unprocessable     → Input is structurally valid but breaks a business rule.
Entity                  "Policy POL-001 is already cancelled."
                        "Coverage amount exceeds the maximum allowed for product HOME."
```

Never use `400` for business rule violations. Never use `422` for missing/invalid fields.

---

## Global Exception Handling Middleware

```csharp
// Api/Middleware/ExceptionHandlingMiddleware.cs
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex, logger);
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        ILogger logger)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var (statusCode, title, detail, logLevel) = exception switch
        {
            ValidationException ve       => (400, "Bad Request",            "One or more validation errors occurred.", LogLevel.Warning),
            NotFoundException nfe        => (404, "Not Found",              nfe.Message,                               LogLevel.Warning),
            ConflictException ce         => (409, "Conflict",               ce.Message,                                LogLevel.Warning),
            DomainException de           => (422, "Unprocessable Entity",   de.Message,                                LogLevel.Warning),
            UnauthorizedAccessException  => (403, "Forbidden",              "You do not have permission to perform this action.", LogLevel.Warning),
            OperationCanceledException   => (499, "Request Cancelled",      null,                                      LogLevel.Information),
            _                            => (500, "Internal Server Error",  null,                                      LogLevel.Error)
        };

        logger.Log(
            logLevel,
            exception,
            "Request {Method} {Path} failed with {StatusCode}: {ExceptionType}",
            context.Request.Method,
            context.Request.Path,
            statusCode,
            exception.GetType().Name);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        if (exception is ValidationException validationException)
        {
            var errors = validationException.Errors
                .GroupBy(e => ToCamelCase(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            var validationProblem = new ValidationProblemDetails(errors)
            {
                Type     = ProblemTypeUri(statusCode),
                Title    = title,
                Status   = statusCode,
                Detail   = detail,
                Instance = context.Request.Path,
            };
            validationProblem.Extensions["traceId"] = traceId;

            await context.Response.WriteAsJsonAsync(validationProblem);
            return;
        }

        var problem = new ProblemDetails
        {
            Type     = ProblemTypeUri(statusCode),
            Title    = title,
            Status   = statusCode,
            Detail   = detail,   // null for 500 — intentional
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;

        await context.Response.WriteAsJsonAsync(problem);
    }

    private static string ProblemTypeUri(int statusCode) => statusCode switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        422 => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        _   => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
    };

    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
```

### Registration in Program.cs

```csharp
// ExceptionHandlingMiddleware must be the outermost middleware — before everything else
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

---

## Validation Error Format from FluentValidation

FluentValidation errors are surfaced via `ValidationBehaviour` in the MediatR pipeline. The middleware catches the thrown `ValidationException` and maps it to `ValidationProblemDetails`.

### Field Name Transformation

FluentValidation uses the C# property name (`CoverageAmount`). The error response must use `camelCase` (`coverageAmount`) to match the JSON API contract. The middleware applies `ToCamelCase` during the mapping.

### Example: Multiple Field Errors

Command:
```csharp
new CreatePolicyCommand(
    CustomerId: "",          // empty
    ProductCode: "home",     // must be uppercase
    EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),  // past date
    CoverageAmount: -500m    // negative
)
```

Response (`400 Bad Request`):
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/policies",
  "traceId": "00-abc123",
  "errors": {
    "customerId": ["'Customer Id' must not be empty."],
    "productCode": ["Product code must contain only uppercase letters."],
    "effectiveDate": ["Effective date must be today or in the future."],
    "coverageAmount": ["Coverage amount must be greater than zero."]
  }
}
```

### Validator Error Messages

```csharp
public sealed class CreatePolicyCommandValidator : AbstractValidator<CreatePolicyCommand>
{
    public CreatePolicyCommandValidator()
    {
        // Every rule that is not self-evident must have an explicit .WithMessage()
        RuleFor(x => x.CustomerId).NotEmpty();

        RuleFor(x => x.ProductCode)
            .NotEmpty()
            .Matches(@"^[A-Z]{2,20}$")
            .WithMessage("Product code must contain only uppercase letters (2–20 characters).");

        RuleFor(x => x.EffectiveDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Effective date must be today or in the future.");

        RuleFor(x => x.CoverageAmount)
            .GreaterThan(0)
            .WithMessage("Coverage amount must be greater than zero.");
    }
}
```

---

## Handling Specific Error Scenarios

### Not Found (404)

Throw `NotFoundException` from the handler when a required resource does not exist. Never return `null` to the controller.

```csharp
// ✅ RIGHT
var policy = await _policyRepository.GetByIdAsync(query.PolicyId, ct)
    ?? throw new NotFoundException(nameof(Policy), query.PolicyId);

// ❌ WRONG — returning null forces every controller to check
var policy = await _policyRepository.GetByIdAsync(query.PolicyId, ct);
return policy is null ? null : policy.ToDto();
```

Response:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Policy with identifier 'POL-999' was not found.",
  "instance": "/api/v1/policies/POL-999",
  "traceId": "00-abc123"
}
```

### Conflict (409)

Throw `ConflictException` when an operation would violate a uniqueness constraint or create a duplicate resource.

```csharp
// In the handler, after checking for duplicates
var existing = await _policyRepository.GetByPolicyNumberAsync(command.PolicyNumber, ct);
if (existing is not null)
    throw new ConflictException($"A policy with number '{command.PolicyNumber}' already exists.");
```

Response:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "A policy with number 'POL-2025-001' already exists.",
  "instance": "/api/v1/policies",
  "traceId": "00-abc123"
}
```

### Domain / Business Rule Violation (422)

Domain entities throw `DomainException` when a state transition violates a business invariant. This propagates through the handler untouched.

```csharp
// Domain entity
public void Cancel(string reason)
{
    if (Status == PolicyStatus.Cancelled)
        throw new DomainException("Policy is already cancelled.");
    if (Status == PolicyStatus.Expired)
        throw new DomainException("An expired policy cannot be cancelled.");
    Status = PolicyStatus.Cancelled;
}
```

Response:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "Policy is already cancelled.",
  "instance": "/api/v1/policies/POL-001/cancel",
  "traceId": "00-abc123"
}
```

### Internal Server Error (500)

Any unhandled exception that is not one of the typed exceptions above maps to `500`. The `detail` field is **always null** on `500` responses.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "Internal Server Error",
  "status": 500,
  "instance": "/api/v1/policies",
  "traceId": "00-abc123"
}
```

---

## What Must Never Be Exposed in Error Responses

The following must **never** appear in any HTTP error response body, regardless of environment:

| Prohibited content | Why |
|---|---|
| Stack traces | Reveals internal code structure and call paths |
| Exception type names (e.g., `NullReferenceException`) | Reveals internal implementation details |
| Connection strings | Exposes database credentials and hostnames |
| SQL query text | Reveals schema and query structure |
| Internal server hostnames or IP addresses | Infrastructure reconnaissance |
| File system paths | Reveals deployment structure |
| EF Core or third-party library error messages verbatim | May contain schema or query details |
| `InnerException.Message` for unhandled exceptions | May contain any of the above |

### Environment Check Anti-Pattern

```csharp
// ❌ WRONG — stack traces must never reach the response, even in Development
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();  // disable this; use ExceptionHandlingMiddleware instead
}
```

```csharp
// ✅ RIGHT — ExceptionHandlingMiddleware is used in all environments
app.UseMiddleware<ExceptionHandlingMiddleware>();
```

Developer diagnostics belong in **log output**, not HTTP responses. The `traceId` in the response is sufficient for a developer to correlate the request with the full stack trace in the logs.

---

## Logging Requirements for Errors

### Log Levels by Exception Type

| Exception | Log level | Rationale |
|---|---|---|
| `ValidationException` | `Warning` | Expected; client error |
| `NotFoundException` | `Warning` | Expected; resource may not exist |
| `ConflictException` | `Warning` | Expected; duplicate or concurrent edit |
| `DomainException` | `Warning` | Expected; business rule enforced |
| `UnauthorizedAccessException` | `Warning` | Expected; auth system handles it |
| `OperationCanceledException` | `Information` | Normal; client disconnected |
| Any other `Exception` | `Error` | Unexpected; requires investigation |

### Required Log Properties

Every exception log entry must include:

```csharp
logger.Log(
    logLevel,
    exception,                          // full exception with stack trace — goes to log, not response
    "Request {Method} {Path} failed with {StatusCode}: {ExceptionType}",
    context.Request.Method,             // GET, POST, etc.
    context.Request.Path,               // /api/v1/policies/POL-001
    statusCode,                         // 404, 422, 500
    exception.GetType().Name);          // NotFoundException, DomainException
```

The `CorrelationId` is automatically included in all log entries by `CorrelationIdMiddleware` via `LogContext.PushProperty`.

### What Must Be Logged for 500 Errors

For unhandled `500` errors, the log entry must capture the full exception (message + stack trace + inner exceptions). This is accomplished by passing the `Exception` instance as the first argument to `logger.Log` / `logger.LogError`.

```csharp
// ✅ RIGHT — exception passed as structured property; Serilog captures full stack trace
logger.LogError(exception, "Unhandled exception for {Method} {Path}", method, path);

// ❌ WRONG — only the message is logged; stack trace is lost
logger.LogError("Unhandled exception: {Message}", exception.Message);
```

### Never Log Sensitive Data

```csharp
// ❌ WRONG — request body may contain sensitive fields
logger.LogWarning("Validation failed for request: {@Request}", command);

// ✅ RIGHT — log only the identifying type and correlation; never the full payload
logger.LogWarning("Validation failed for {RequestType}", typeof(CreatePolicyCommand).Name);
```

---

## Controller Error Handling Rules

### No try/catch in Controllers

```csharp
// ❌ WRONG — controller handles exceptions directly
[HttpGet("{policyId}")]
public async Task<IActionResult> GetById(string policyId, CancellationToken ct)
{
    try
    {
        var dto = await _sender.Send(new GetPolicyByIdQuery(policyId), ct);
        return Ok(dto.ToResponse());
    }
    catch (NotFoundException)
    {
        return NotFound();   // loses ProblemDetails shape and traceId
    }
}

// ✅ RIGHT — let the middleware handle it
[HttpGet("{policyId}")]
public async Task<IActionResult> GetById(string policyId, CancellationToken ct)
{
    var dto = await _sender.Send(new GetPolicyByIdQuery(policyId), ct);
    return Ok(dto.ToResponse());
}
```

### ProducesResponseType Must Match Reality

Every documented error response in `[ProducesResponseType]` must use `ProblemDetails` or `ValidationProblemDetails`:

```csharp
[HttpPost]
[ProducesResponseType(typeof(PolicyResponse),          StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ValidationProblemDetails),StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails),          StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails),          StatusCodes.Status422UnprocessableEntity)]
[ProducesResponseType(typeof(ProblemDetails),          StatusCodes.Status500InternalServerError)]
public async Task<IActionResult> Create([FromBody] CreatePolicyRequest request, CancellationToken ct)
```

---

## Enforcement Checklist

Before raising a PR that adds or modifies error-producing code:

- [ ] All new exception types extend the correct base (`DomainException`, `NotFoundException`, `ConflictException`, or `ValidationException`)
- [ ] No `try/catch` blocks in controllers
- [ ] No `try/catch` in handlers that silently swallows exceptions
- [ ] `ExceptionHandlingMiddleware` is the outermost middleware in `Program.cs`
- [ ] `500` responses never include `detail`, exception messages, or stack traces
- [ ] `400` responses use `ValidationProblemDetails` and include the `errors` map with `camelCase` keys
- [ ] `422` is used for business rule violations; `400` is used for structural/format validation failures
- [ ] All error responses set `Content-Type: application/problem+json`
- [ ] Log level is `Warning` for expected domain/client errors; `Error` for unhandled exceptions
- [ ] Exception instances (not just `.Message`) are passed to `logger.Log` so stack traces are captured
- [ ] No sensitive data (payloads, credentials, connection strings) in log statements
- [ ] `[ProducesResponseType]` attributes on controllers reference `ProblemDetails` / `ValidationProblemDetails` for error status codes
