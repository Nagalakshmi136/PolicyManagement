---
applyTo: "src/PolicyManagement.Infrastructure/**/*.cs"
---

# Database and EF Core Standards — PolicyManagement BFF

## Stack

| Component | Version |
|---|---|
| Entity Framework Core | 9.x |
| EF Core SQL Server provider | 9.x |
| SQL Server | 2022 (Developer / Azure SQL) |
| EF Core Tools | 9.x (`dotnet-ef`) |

---

## Project Placement Rules

All database concerns live exclusively in `PolicyManagement.Infrastructure`. No other project may reference EF Core types.

```
PolicyManagement.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── Configurations/               ← IEntityTypeConfiguration<T> per entity
│   │   ├── PolicyConfiguration.cs
│   │   ├── ClaimConfiguration.cs
│   │   └── CustomerConfiguration.cs
│   ├── Repositories/                 ← concrete repository implementations
│   │   ├── PolicyRepository.cs
│   │   └── ClaimRepository.cs
│   ├── Migrations/                   ← generated only — never hand-edited
│   └── Seed/
│       ├── DataSeeder.cs
│       └── Data/
│           ├── customers.json
│           └── products.json
└── DependencyInjection.cs
```

---

## Naming Conventions

### Tables

| Rule | Convention | Example |
|---|---|---|
| Table name | `PascalCase`, plural noun | `Policies`, `Claims`, `Customers` |
| Junction / join table | Both entity names, alphabetical order | `PolicyCoverages` |
| Audit / log tables | Suffixed `_Audit` or `_History` | `Policies_Audit` |

### Columns

| Rule | Convention | Example |
|---|---|---|
| Column name | `PascalCase` | `PolicyId`, `EffectiveDate` |
| Primary key | `{EntityName}Id` | `PolicyId`, `ClaimId` |
| Foreign key | `{ReferencedEntity}Id` | `CustomerId`, `PolicyId` |
| Soft-delete flag | `IsDeleted` (bit, not null, default 0) | `IsDeleted` |
| Audit timestamps | `CreatedAt`, `UpdatedAt` (datetime2, not null) | `CreatedAt` |
| Boolean flags | `Is{State}` or `Has{Feature}` | `IsActive`, `HasBeenReviewed` |

### Constraint Names

```
PK_{Table}                          PK_Policies
FK_{Table}_{ReferencedTable}        FK_Policies_Customers
IX_{Table}_{Column(s)}              IX_Policies_CustomerId
IX_{Table}_{Column(s)}_U            IX_Policies_PolicyNumber_U   (unique)
CK_{Table}_{Rule}                   CK_Policies_CoverageAmountPositive
```

Always specify constraint names explicitly in `IEntityTypeConfiguration` — never rely on EF Core's generated names.

---

## Entity Configuration Conventions

### Rule: All Configuration via `IEntityTypeConfiguration<T>`

Never configure entities using Data Annotations (`[Key]`, `[Required]`, `[Column]`) on domain entity classes. All mapping belongs in the Infrastructure layer.

```csharp
// ✅ RIGHT — configuration in Infrastructure
// Infrastructure/Persistence/Configurations/PolicyConfiguration.cs
internal sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("Policies");

        builder.HasKey(p => p.Id)
            .HasName("PK_Policies");

        builder.Property(p => p.Id)
            .HasColumnName("PolicyId")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.PolicyNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(p => p.PolicyNumber)
            .IsUnique()
            .HasDatabaseName("IX_Policies_PolicyNumber_U");

        builder.Property(p => p.CustomerId)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(p => p.CustomerId)
            .HasDatabaseName("IX_Policies_CustomerId");

        builder.Property(p => p.ProductCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()       // store as string, not int
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.CoverageAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.EffectiveDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.ExpiryDate)
            .HasColumnType("date");

        builder.Property(p => p.CreatedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetime2");

        // Value object owned entity
        builder.OwnsOne(p => p.Address, address =>
        {
            address.Property(a => a.Street).HasColumnName("AddressStreet").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("AddressCity").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("AddressPostalCode").HasMaxLength(20);
        });

        // Soft delete query filter
        builder.HasQueryFilter(p => !p.IsDeleted);

        // Relationships
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Policies_Customers");

        builder.HasMany(p => p.Claims)
            .WithOne()
            .HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_Claims_Policies");
    }
}
```

### Auto-Registration in AppDbContext

```csharp
// Infrastructure/Persistence/AppDbContext.cs
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Policy>   Policies  { get; init; }
    public DbSet<Claim>    Claims    { get; init; }
    public DbSet<Customer> Customers { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Auto-discover all IEntityTypeConfiguration<T> in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

### Configuration Rules

| Rule | Detail |
|---|---|
| All monetary values | `decimal(18,2)` — never `float` or `money` |
| All date-only fields | `date` column type, `DateOnly` C# property |
| All date-time fields | `datetime2` — never `datetime` |
| Enum storage | Store as `string` (`.HasConversion<string>()`) — never as raw `int` |
| String columns | Always set `HasMaxLength` — never unbounded `nvarchar(max)` unless justified |
| Nullable columns | Explicitly call `.IsRequired()` for non-nullable; omit for nullable |
| Cascade delete | Default to `DeleteBehavior.Restrict`; use `Cascade` only for true ownership |
| Soft delete | Apply `HasQueryFilter(e => !e.IsDeleted)` globally on all soft-deletable entities |

---

## AppDbContext Rules — What Must Never Go in DbContext Directly

| ❌ Never in AppDbContext | ✅ Where it belongs |
|---|---|
| `OnModelCreating` fluent configuration for specific entities | `IEntityTypeConfiguration<T>` classes |
| Business logic or domain validation | Domain entity methods |
| Repository query methods | `IRepository` implementations |
| Raw SQL queries (`FromSqlRaw`) | Repository methods (encapsulated, named) |
| Seeding large datasets inline | `DataSeeder` class called from migrations or startup |
| `SaveChanges` override with audit logic | `AuditableDbContext` base class or `SaveChangesInterceptor` |

### Audit Interceptor (preferred over override)

```csharp
// Infrastructure/Persistence/AuditSaveChangesInterceptor.cs
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static void UpdateAuditFields(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
    }
}
```

Register via `DbContextOptionsBuilder.AddInterceptors(...)` in `DependencyInjection.cs`.

---

## Migration Workflow

### Golden Rule: Never Edit Migration Files by Hand

Migrations are generated artifacts. Editing them manually introduces drift between the model snapshot and the actual database. All schema changes flow through the EF Core tooling.

### Commands

```bash
# Add a new migration (run from solution root)
dotnet ef migrations add <MigrationName> \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api \
  --output-dir Persistence/Migrations

# Apply pending migrations to the database
dotnet ef database update \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api

# Generate a SQL script for production deployment review
dotnet ef migrations script \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api \
  --idempotent \
  --output migrations.sql

# Remove the last unapplied migration
dotnet ef migrations remove \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api

# List all migrations and their status
dotnet ef migrations list \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api
```

### Migration Naming Convention

```
{YYYYMMDD}_{PascalCaseDescription}

20250610_InitialSchema
20250615_AddPolicyCancellationReason
20250620_AddClaimStatusIndex
20250701_AddCustomerAddressColumns
```

### Applying Migrations in Production

```csharp
// Program.cs — apply migrations at startup (development / CI only)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Integration"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

In production, use `dotnet ef migrations script --idempotent` to generate a reviewed SQL script deployed by the CI/CD pipeline — never auto-migrate in production.

### Migration Rules

- Migration names are descriptive and date-prefixed
- Each migration does one logical thing — no "fix everything" mega-migrations
- `Down()` method must be implemented and must correctly reverse `Up()`
- After adding a migration, always review the generated file before committing — verify no unintended column drops or renames
- Schema changes that require data backfill get a migration for the schema change AND a separate migration for the data transformation

---

## Repository Pattern Conventions

### Interface (Domain / Application layer)

```csharp
// Domain/Repositories/IPolicyRepository.cs
public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(string policyId, CancellationToken ct = default);
    Task<Policy?> GetByPolicyNumberAsync(string policyNumber, CancellationToken ct = default);
    Task<PagedResult<Policy>> GetByCustomerIdAsync(string customerId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<Policy>> SearchAsync(PolicySearchCriteria criteria, CancellationToken ct = default);
    Task AddAsync(Policy policy, CancellationToken ct = default);
    Task UpdateAsync(Policy policy, CancellationToken ct = default);
    Task DeleteAsync(string policyId, CancellationToken ct = default);
}
```

### Implementation (Infrastructure layer)

```csharp
// Infrastructure/Persistence/Repositories/PolicyRepository.cs
internal sealed class PolicyRepository(AppDbContext db) : IPolicyRepository
{
    public async Task<Policy?> GetByIdAsync(string policyId, CancellationToken ct = default) =>
        await db.Policies
            .Include(p => p.Claims)
            .FirstOrDefaultAsync(p => p.PolicyId == policyId, ct);

    public async Task<PagedResult<Policy>> GetByCustomerIdAsync(
        string customerId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Policies
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Policy>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<Policy>> SearchAsync(PolicySearchCriteria criteria, CancellationToken ct = default)
    {
        var query = db.Policies.AsQueryable();

        if (!string.IsNullOrEmpty(criteria.CustomerId))
            query = query.Where(p => p.CustomerId == criteria.CustomerId);

        if (criteria.Status.HasValue)
            query = query.Where(p => p.Status == criteria.Status.Value);

        if (criteria.EffectiveDateFrom.HasValue)
            query = query.Where(p => p.EffectiveDate >= criteria.EffectiveDateFrom.Value);

        if (criteria.EffectiveDateTo.HasValue)
            query = query.Where(p => p.EffectiveDate <= criteria.EffectiveDateTo.Value);

        query = criteria.SortDirection == SortDirection.Descending
            ? query.OrderByDescending(SortExpression(criteria.SortBy))
            : query.OrderBy(SortExpression(criteria.SortBy));

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Policy>(items, totalCount, criteria.Page, criteria.PageSize);
    }

    public async Task AddAsync(Policy policy, CancellationToken ct = default) =>
        await db.Policies.AddAsync(policy, ct);

    public async Task UpdateAsync(Policy policy, CancellationToken ct = default) =>
        db.Policies.Update(policy);

    public async Task DeleteAsync(string policyId, CancellationToken ct = default)
    {
        var policy = await GetByIdAsync(policyId, ct);
        if (policy is not null)
            db.Policies.Remove(policy);
    }

    private static Expression<Func<Policy, object>> SortExpression(string? sortBy) =>
        sortBy?.ToLowerInvariant() switch
        {
            "effectivedate" => p => p.EffectiveDate,
            "coverageamount" => p => p.CoverageAmount,
            "status" => p => p.Status,
            _ => p => p.CreatedAt    // default sort
        };
}
```

### Repository Rules

- Repository implementations are `internal sealed`
- `SaveChangesAsync` is **never called in a repository** — it is called by `IUnitOfWork` in the Application handler
- `AddAsync` / `UpdateAsync` / `DeleteAsync` only track changes on the `ChangeTracker` — they do not save
- Use `AsNoTracking()` on all read-only query paths that do not need change tracking
- `Include` navigation properties explicitly — never rely on lazy loading
- Repositories return domain entities (or `PagedResult<T>`) — they never return DTOs, anonymous types, or `IQueryable<T>`
- No raw SQL (`FromSqlRaw`, `ExecuteSqlRaw`) in repositories without a documented justification in a code comment

### Unit of Work

```csharp
// Application/Common/Interfaces/IUnitOfWork.cs
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

// Infrastructure/Persistence/UnitOfWork.cs
internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
```

---

## Index Design

### Required Indexes

Every filterable, sortable, or join column must have an explicit index defined in its `IEntityTypeConfiguration<T>`.

| Table | Column(s) | Index type | Reason |
|---|---|---|---|
| `Policies` | `PolicyNumber` | Unique | Natural key lookups |
| `Policies` | `CustomerId` | Non-unique | Filter by customer |
| `Policies` | `Status` | Non-unique | Filter by status |
| `Policies` | `EffectiveDate` | Non-unique | Date range filters |
| `Policies` | `CreatedAt` | Non-unique | Default sort |
| `Policies` | `(CustomerId, Status)` | Composite | Combined filter queries |
| `Claims` | `PolicyId` | Non-unique | FK join performance |
| `Claims` | `Status` | Non-unique | Filter by claim status |
| `Claims` | `FiledDate` | Non-unique | Date range filters |
| `Customers` | `Email` | Unique | Lookup + uniqueness |

### Composite Index Rule

Create a composite index when queries regularly filter on two columns together and the selectivity of the combined condition is significantly higher than either column alone.

```csharp
// Example: policies are frequently listed by customer AND filtered by status
builder.HasIndex(p => new { p.CustomerId, p.Status })
    .HasDatabaseName("IX_Policies_CustomerId_Status");
```

### Index Configuration Example

```csharp
// All indexes go in the entity's IEntityTypeConfiguration<T>
builder.HasIndex(p => p.PolicyNumber)
    .IsUnique()
    .HasDatabaseName("IX_Policies_PolicyNumber_U");

builder.HasIndex(p => p.CustomerId)
    .HasDatabaseName("IX_Policies_CustomerId");

builder.HasIndex(p => p.Status)
    .HasDatabaseName("IX_Policies_Status");

builder.HasIndex(p => p.EffectiveDate)
    .HasDatabaseName("IX_Policies_EffectiveDate");

builder.HasIndex(p => p.CreatedAt)
    .HasDatabaseName("IX_Policies_CreatedAt");

builder.HasIndex(p => new { p.CustomerId, p.Status })
    .HasDatabaseName("IX_Policies_CustomerId_Status");
```

---

## Seed Data Approach (200+ Records)

For datasets of 200+ records, do not use `modelBuilder.HasData()` — it embeds all rows in the migration file, making migrations unreadable and fragile. Use a dedicated `DataSeeder` invoked at application startup.

### DataSeeder Pattern

```csharp
// Infrastructure/Persistence/Seed/DataSeeder.cs
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger)
    {
        await SeedCustomersAsync(db, logger);
        await SeedProductCodesAsync(db, logger);
        await SeedPoliciesAsync(db, logger);
    }

    private static async Task SeedCustomersAsync(AppDbContext db, ILogger logger)
    {
        if (await db.Customers.AnyAsync()) return;   // idempotent guard

        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Seed", "Data", "customers.json"));

        var customers = JsonSerializer.Deserialize<List<CustomerSeedDto>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialise customers seed data.");

        await db.Customers.AddRangeAsync(customers.Select(c => new Customer
        {
            Id    = c.Id,
            Name  = c.Name,
            Email = c.Email,
        }));

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} customers", customers.Count);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
```

### Seed Data JSON Format

```json
// Infrastructure/Persistence/Seed/Data/customers.json
[
  { "id": "CUST-001", "name": "Acme Corporation", "email": "contact@acme.com" },
  { "id": "CUST-002", "name": "Globex Insurance", "email": "info@globex.com" }
]
```

### Invoking the Seeder

```csharp
// Program.cs
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Integration"))
{
    using var scope = app.Services.CreateScope();
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db, logger);
}
```

### Seed Data Rules

- All seed methods are **idempotent** — check `AnyAsync()` before inserting
- Seed JSON files are committed to source control in `Infrastructure/Persistence/Seed/Data/`
- Seed data files are embedded resources or copied to the output directory via `.csproj`:
  ```xml
  <ItemGroup>
    <Content Include="Persistence\Seed\Data\**\*.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  ```
- Reference data (product codes, statuses, regions) lives in seed JSON; test/demo data is seeded only in non-production environments
- `modelBuilder.HasData()` is acceptable only for small, stable reference tables (< 20 rows) that are tightly coupled to the schema

---

## DependencyInjection Registration

```csharp
// Infrastructure/DependencyInjection.cs
public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddDbContext<AppDbContext>(options =>
        options
            .UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null))
            .AddInterceptors(new AuditSaveChangesInterceptor()));

    services.AddScoped<IUnitOfWork, UnitOfWork>();
    services.AddScoped<IPolicyRepository, PolicyRepository>();
    services.AddScoped<IClaimRepository, ClaimRepository>();
    services.AddScoped<ICustomerRepository, CustomerRepository>();

    return services;
}
```

---

## Enforcement Checklist

Before raising a PR that adds or modifies a database entity, configuration, or migration:

- [ ] All entity mapping uses `IEntityTypeConfiguration<T>` — no Data Annotations on domain entities
- [ ] `ApplyConfigurationsFromAssembly` is used in `AppDbContext.OnModelCreating` — no inline `modelBuilder.Entity<T>()` calls
- [ ] All monetary columns use `decimal(18,2)`; all timestamps use `datetime2`; all enums stored as `string`
- [ ] All string columns have an explicit `HasMaxLength`
- [ ] All filterable and sortable columns have a named index in their configuration
- [ ] All index and constraint names follow the naming convention (`PK_`, `FK_`, `IX_`, `CK_`)
- [ ] Migration added via `dotnet ef migrations add` — migration file not hand-edited
- [ ] Migration name is date-prefixed and descriptive
- [ ] `Down()` method correctly reverses `Up()`
- [ ] Repository implementations are `internal sealed` and never call `SaveChangesAsync`
- [ ] Read-only repository methods use `AsNoTracking()`
- [ ] Seed data methods are idempotent (`AnyAsync()` guard before insert)
- [ ] No business logic, query methods, or raw SQL in `AppDbContext` directly
