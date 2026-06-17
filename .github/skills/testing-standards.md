---
applyTo: "tests/**/*.cs,src/**/*.cs"
---

# Testing Standards — PolicyManagement BFF

## Stack

| Library | Version | Purpose |
|---|---|---|
| xUnit | 2.x | Test runner and assertions framework |
| FluentAssertions | 6.x | Readable assertion DSL |
| Moq | 4.x | Mocking interfaces and dependencies |
| WebApplicationFactory | ASP.NET Core | In-process integration test host |
| Microsoft.EntityFrameworkCore.InMemory | — | Lightweight DB substitute for unit tests |

---

## Test Project Layout

```
tests/
├── PolicyManagement.Domain.Tests/
│   └── Entities/
│       ├── PolicyTests.cs
│       └── ClaimTests.cs
├── PolicyManagement.Application.Tests/
│   └── Policies/
│       ├── Commands/
│       │   └── CreatePolicyCommandHandlerTests.cs
│       └── Queries/
│           └── GetPolicyByIdQueryHandlerTests.cs
├── PolicyManagement.Infrastructure.Tests/
│   └── Repositories/
│       └── PolicyRepositoryTests.cs
└── PolicyManagement.Api.Tests/
    ├── Controllers/
    │   └── PoliciesControllerTests.cs      ← WebApplicationFactory tests
    └── Common/
        ├── ApiWebApplicationFactory.cs
        └── SeedData.cs
```

Mirror the `src/` folder structure inside each test project. A test file maps 1-to-1 to the production type it tests.

---

## Naming Conventions

### Test Class Names

`<TypeUnderTest>Tests`

```csharp
public class PolicyTests { }                         // Domain entity
public class CreatePolicyCommandHandlerTests { }     // Application handler
public class PolicyRepositoryTests { }               // Infrastructure repository
public class PoliciesControllerTests { }             // API controller (integration)
```

### Test Method Names

Pattern: `<MethodOrScenario>_<StateOrInput>_<ExpectedOutcome>`

```csharp
// Domain
Cancel_WhenPolicyIsActive_ShouldSetStatusToCancelled()
Cancel_WhenPolicyAlreadyCancelled_ShouldThrowDomainException()
Create_WhenEffectiveDateIsInThePast_ShouldThrowDomainException()

// Application handler
Handle_WhenCustomerExists_ShouldCreateAndReturnPolicy()
Handle_WhenCustomerNotFound_ShouldThrowNotFoundException()

// Repository
GetByIdAsync_WhenPolicyExists_ShouldReturnPolicy()
GetByIdAsync_WhenPolicyDoesNotExist_ShouldReturnNull()

// Integration / API
POST_Policies_WithValidRequest_Returns201WithLocation()
POST_Policies_WithMissingRequiredField_Returns400WithErrorsMap()
GET_Policies_PolicyId_WhenNotFound_Returns404ProblemDetails()
```

**Rules:**
- Use underscores as separators (readability in test output)
- The method name must be self-documenting — a failing test name alone should tell the reader what is broken
- Do not use abbreviations (use `WhenPolicyAlreadyCancelled`, not `WhenAlreadyCanc`)

### Arrange / Act / Assert Structure

Every test must follow AAA with blank-line separation and optional comments on non-obvious sections:

```csharp
[Fact]
public void Cancel_WhenPolicyIsActive_ShouldSetStatusToCancelled()
{
    // Arrange
    var policy = PolicyFactory.CreateActive();

    // Act
    policy.Cancel("Customer request");

    // Assert
    policy.Status.Should().Be(PolicyStatus.Cancelled);
    policy.DomainEvents.Should().ContainSingle(e => e is PolicyCancelledEvent);
}
```

---

## Unit Test Structure

### Domain Layer Tests

Test **entity behaviour and invariants** in isolation — no mocks, no DI, no database.

```csharp
public class PolicyTests
{
    [Fact]
    public void Cancel_WhenPolicyIsActive_ShouldSetStatusToCancelled()
    {
        // Arrange
        var policy = PolicyFactory.CreateActive();

        // Act
        policy.Cancel("Customer request");

        // Assert
        policy.Status.Should().Be(PolicyStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenPolicyAlreadyCancelled_ShouldThrowDomainException()
    {
        // Arrange
        var policy = PolicyFactory.CreateCancelled();

        // Act
        var act = () => policy.Cancel("Duplicate");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*already cancelled*");
    }

    [Theory]
    [MemberData(nameof(InvalidCoverageAmounts))]
    public void Create_WithInvalidCoverageAmount_ShouldThrowDomainException(decimal amount)
    {
        var act = () => Policy.Create("CUST-001", "HOME", DateOnly.FromDateTime(DateTime.UtcNow), amount);

        act.Should().Throw<DomainException>();
    }

    public static TheoryData<decimal> InvalidCoverageAmounts => new() { 0m, -1m, -100m };
}
```

**Rules:**
- No mocks in Domain tests — domain logic must be pure functions/state transitions
- Use `[Theory]` + `[InlineData]` / `[MemberData]` for boundary value tests
- Assert domain events were raised where applicable

### Application Layer Tests

Test **use-case orchestration** — mock all ports (repositories, external services); verify the handler calls ports correctly and returns expected output.

```csharp
public class CreatePolicyCommandHandlerTests
{
    private readonly Mock<IPolicyRepository> _policyRepositoryMock = new();
    private readonly Mock<ICustomerRepository> _customerRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CreatePolicyCommandHandler _sut;

    public CreatePolicyCommandHandlerTests()
    {
        _sut = new CreatePolicyCommandHandler(
            _policyRepositoryMock.Object,
            _customerRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task Handle_WhenCustomerExists_ShouldCreateAndReturnPolicy()
    {
        // Arrange
        var customer = CustomerFactory.CreateDefault();
        _customerRepositoryMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var command = new CreatePolicyCommand(customer.Id, "HOME", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), 50_000m);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CustomerId.Should().Be(customer.Id);
        _policyRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Policy>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCustomerNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        _customerRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new CreatePolicyCommand("UNKNOWN", "HOME", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), 50_000m);

        // Act
        var act = async () => await _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Customer*UNKNOWN*");
    }
}
```

**Rules:**
- Declare mocks as `readonly` fields; construct the SUT (`_sut`) in the constructor
- Use `Verify(..., Times.Once)` / `Times.Never` to assert side effects (saves, emails, events)
- Use `It.IsAny<CancellationToken>()` for cancellation token parameters in mock setups — never `CancellationToken.None` directly in `Setup`
- Do not test FluentValidation rules in handler tests — validators have their own test class

### FluentValidation Tests

```csharp
public class CreatePolicyCommandValidatorTests
{
    private readonly CreatePolicyCommandValidator _sut = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var command = CommandFactory.ValidCreatePolicy();

        var result = _sut.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WhenCustomerIdIsEmpty_ShouldHaveValidationError(string? customerId)
    {
        var command = CommandFactory.ValidCreatePolicy() with { CustomerId = customerId! };

        var result = _sut.Validate(command);

        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePolicyCommand.CustomerId));
    }
}
```

---

## Integration Test Structure (WebApplicationFactory)

Integration tests exercise the full HTTP pipeline: middleware, routing, model binding, DI, and (optionally) the database.

### Base Factory

```csharp
// tests/PolicyManagement.Api.Tests/Common/ApiWebApplicationFactory.cs
public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private AppDbContext _dbContext = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Replace real DbContext with in-memory or test container
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
        });
    }

    public async Task InitializeAsync()
    {
        _dbContext = Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
        await _dbContext.Database.EnsureCreatedAsync();
        await SeedData.SeedAsync(_dbContext);
    }

    public new async Task DisposeAsync() => await _dbContext.Database.EnsureDeletedAsync();
}
```

### Test Class Pattern

```csharp
// tests/PolicyManagement.Api.Tests/Controllers/PoliciesControllerTests.cs
public class PoliciesControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PoliciesControllerTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_Policies_WithValidRequest_Returns201WithLocation()
    {
        // Arrange
        var request = new CreatePolicyRequest(
            CustomerId: SeedData.ExistingCustomerId,
            ProductCode: "HOME",
            EffectiveDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            CoverageAmount: 50_000m
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/policies", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<PolicyResponse>();
        body!.CustomerId.Should().Be(SeedData.ExistingCustomerId);
    }

    [Fact]
    public async Task POST_Policies_WithMissingRequiredField_Returns400WithErrorsMap()
    {
        // Arrange
        var request = new { ProductCode = "HOME" }; // customerId missing

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/policies", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("customerId");
    }

    [Fact]
    public async Task GET_Policies_PolicyId_WhenNotFound_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/policies/NON-EXISTENT-ID");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(404);
        problem.Title.Should().Be("Not Found");
    }
}
```

**Rules:**
- Use `IClassFixture<ApiWebApplicationFactory>` — one factory instance per test class (not per test)
- Never share `HttpClient` state between tests; create it fresh from `factory.CreateClient()` per test class
- Assert HTTP status code first, then deserialise and assert the body
- Assert `Content-Type: application/problem+json` on error responses
- Test the full round-trip: a `POST` that creates a resource should be followed by a `GET` to confirm persistence

### Authenticated Endpoints

All integration tests must replace the Keycloak JWT Bearer scheme with a passthrough `TestAuthHandler`. Tests that exercise role-based access must set the appropriate role claims.

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureTestServices(services =>
    {
        // Replace JWT auth with a test scheme that always authenticates
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });
    });
}
```

```csharp
// tests/PolicyManagement.Api.Tests/Common/TestAuthHandler.cs
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";

    // Default role for most tests: policy-admin (full access)
    public static string[] DefaultRoles { get; set; } = ["policy-admin"];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "test-user"),
            new(ClaimTypes.NameIdentifier, "user-001"),
        };
        // Add each role as a separate ClaimTypes.Role claim
        claims.AddRange(DefaultRoles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

For tests that need a specific role (e.g., asserting `403` for a reader attempting to flag):

```csharp
[Fact]
public async Task PATCH_Flag_AsReader_Returns403Forbidden()
{
    // Arrange — create a client authenticated as policy-reader only
    var client = _factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication("ReaderAuth")
                .AddScheme<AuthenticationSchemeOptions, ReaderTestAuthHandler>(
                    "ReaderAuth", _ => { });
        })).CreateClient();

    // Act
    var response = await client.PatchAsJsonAsync("/api/v1/policies/flag",
        new { policyIds = new[] { "some-id" } });

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    problem!.Status.Should().Be(403);
}

[Fact]
public async Task GET_Policies_WithNoToken_Returns401Unauthorized()
{
    // Arrange — client with no auth header
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });
    // Override to use a scheme that always fails
    // (or simply remove the TestAuth override in a dedicated factory)

    // Act
    var response = await client.GetAsync("/api/v1/policies");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

---

## What Must Be Tested in Each Layer

### Domain Layer — Required Tests

| Subject | Test scenario |
|---|---|
| Entity creation | Valid input succeeds; each required invariant violation throws `DomainException` |
| State transitions | Each valid state change succeeds; each invalid transition throws |
| Domain events | Correct event is raised with correct data after state change |
| Value objects | Equality, comparison, and validation rules |
| Factory methods | Both happy path and all guarded paths |

### Application Layer — Required Tests

| Subject | Test scenario |
|---|---|
| Command handler — happy path | Correct domain method called, repository `AddAsync`/`UpdateAsync` called, `SaveChangesAsync` called |
| Command handler — not found | `NotFoundException` thrown when a depended-on resource is missing |
| Command handler — domain error | `DomainException` propagated (not swallowed) |
| Query handler | Correct data returned; repository `GetByIdAsync` / `GetAllAsync` called with expected args |
| Validators | Valid input passes; each distinct validation rule fails independently |
| Pagination query | `pageSize` and `page` forwarded correctly to repository |

### Infrastructure Layer — Required Tests

| Subject | Test scenario |
|---|---|
| Repository `AddAsync` + `GetByIdAsync` | Persisted entity retrieved with correct field values |
| Repository `UpdateAsync` | Modified fields persisted; unmodified fields unchanged |
| Repository `DeleteAsync` | Entity no longer retrievable after deletion |
| Soft-delete (if used) | Deleted entity excluded from default queries |
| Filtering / pagination | Results filtered and paged correctly |

Use EF Core InMemory provider for unit-scope repository tests. Use a real SQL Server (Testcontainers) for full fidelity tests if available.

### API Layer — Required Tests

| Subject | Test scenario |
|---|---|
| Happy path — create | `201 Created` + `Location` header + correct response body |
| Happy path — get | `200 OK` + correct resource body |
| Happy path — list | `200 OK` + `data` array + `pagination` meta |
| Happy path — update | `200 OK` or `204 No Content` as per contract |
| Happy path — delete | `204 No Content` |
| Validation failure | `400 Bad Request` + `application/problem+json` + `errors` map |
| Not found | `404 Not Found` + `application/problem+json` |
| Business rule violation | `422 Unprocessable Entity` + `application/problem+json` |
| Unauthenticated | `401 Unauthorized` — request with no/invalid token; assert `WWW-Authenticate` header present |
| Insufficient role | `403 Forbidden` — `policy-reader` token calling `PATCH /flag` |
| Elevated role permitted | `200 OK` — `policy-admin` token calling `PATCH /flag` |
| Health endpoints are anonymous | `GET /health/live` returns `200 OK` with no token |
| Pagination params | Correct page returned; out-of-range page returns empty `data` |

---

## Test Data and Seed Data Conventions

### Factory Classes (Unit Tests)

Use static factory classes in a `Common/` or `TestData/` folder within each test project. Never create domain objects via constructors with hard-coded strings scattered across test methods.

```csharp
// tests/PolicyManagement.Domain.Tests/TestData/PolicyFactory.cs
public static class PolicyFactory
{
    public static Policy CreateActive(
        string customerId = "CUST-001",
        string productCode = "HOME",
        decimal coverageAmount = 50_000m) =>
        Policy.Create(customerId, productCode, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), coverageAmount);

    public static Policy CreateCancelled()
    {
        var policy = CreateActive();
        policy.Cancel("Test cancellation");
        return policy;
    }
}
```

### Seed Data (Integration Tests)

Centralise all seed data in a single class per test project. Constants for well-known IDs prevent magic strings leaking into test methods.

```csharp
// tests/PolicyManagement.Api.Tests/Common/SeedData.cs
public static class SeedData
{
    public const string ExistingCustomerId  = "CUST-SEED-001";
    public const string ExistingPolicyId    = "POL-SEED-001";
    public const string CancelledPolicyId   = "POL-SEED-002";

    public static async Task SeedAsync(AppDbContext db)
    {
        db.Customers.Add(new Customer { Id = ExistingCustomerId, Name = "Test Customer" });

        db.Policies.Add(new Policy { Id = ExistingPolicyId, CustomerId = ExistingCustomerId, Status = PolicyStatus.Active, ... });
        db.Policies.Add(new Policy { Id = CancelledPolicyId, CustomerId = ExistingCustomerId, Status = PolicyStatus.Cancelled, ... });

        await db.SaveChangesAsync();
    }
}
```

**Rules:**
- Seed data is **read-only baseline** — tests that mutate data must use their own isolated records, not seed constants
- Use `Guid.NewGuid().ToString()` or a counter to generate IDs for records created within individual tests
- Never share a database instance between test classes when mutation is involved — use `IClassFixture` with a fresh in-memory DB per class

### Builder Pattern for Complex Objects

For entities with many fields, use a builder or `with` expressions on records:

```csharp
var command = CommandFactory.ValidCreatePolicy() with
{
    EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) // force past date
};
```

---

## Coverage Expectations

| Layer | Minimum line coverage | Notes |
|---|---|---|
| Domain | **95%** | Pure logic — close to 100% should be achievable |
| Application | **90%** | All handler paths, all validators |
| Infrastructure | **80%** | Repository CRUD + edge cases |
| API | **80%** | All endpoints × all documented status codes |

**Rules:**
- Coverage is a floor, not a target — do not write meaningless tests to hit a number
- 100% coverage with no behaviour assertions is worthless; prefer fewer, meaningful tests
- Prioritise covering: domain invariants, error paths, and all documented HTTP status codes
- Coverage is measured per PR in CI — a PR that drops any layer below its floor must add tests before merge

### What is Excluded from Coverage

```xml
<!-- in .coveragerc or .runsettings -->
[ExcludeFromCodeCoverage] attribute is valid for:
- Generated code (migrations, scaffolded models)
- Program.cs bootstrapping
- DependencyInjection.cs extension methods
- DTOs / records with no logic
```

Apply `[ExcludeFromCodeCoverage]` sparingly — it is not a coverage escape hatch.

---

## Test Isolation Rules

1. **No shared mutable state** between tests — each `[Fact]` or `[Theory]` case must be fully independent
2. **No file system, network, or real database** in unit tests — mock or use in-memory substitutes
3. **No `Thread.Sleep` or arbitrary delays** — use async/await with `CancellationToken` for timing-sensitive scenarios
4. **No `DateTime.Now` or `Guid.NewGuid()` in production code without abstraction** — inject `IDateTimeProvider` / `IGuidProvider` and mock them in tests
5. **No `[Collection]` shared state** unless you explicitly need ordered fixture lifecycle — prefer `IClassFixture` for per-class isolation

---

## Running Tests

```bash
# Run all tests
dotnet test

# Run a specific project
dotnet test tests/PolicyManagement.Domain.Tests

# Run tests matching a name filter
dotnet test --filter "FullyQualifiedName~CreatePolicyCommandHandlerTests"

# Run with coverage (requires coverlet)
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Verbose output on failure
dotnet test --logger "console;verbosity=detailed"
```

---

## Enforcement Checklist

Before raising a PR that adds or modifies production code:

- [ ] New public methods on domain entities have at least one happy-path and one error-path test
- [ ] New command/query handlers have happy-path, not-found, and domain-error test cases
- [ ] New API endpoints are covered by integration tests for all documented status codes
- [ ] No test uses `Thread.Sleep`, `DateTime.Now`, or `Guid.NewGuid()` in production code paths without injection
- [ ] No magic strings in test methods — use factory classes or `SeedData` constants
- [ ] FluentAssertions used consistently — no raw `Assert.Equal` / `Assert.Throws`
- [ ] All async tests use `async Task` return type — no `async void`
- [ ] Coverage for the modified layer does not drop below its floor
