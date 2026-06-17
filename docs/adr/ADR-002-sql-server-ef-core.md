# ADR-002: SQL Server with EF Core 9 (Code-First, Migrations)

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The service requires a relational database to persist 200+ policy records and support filtered, sorted, paginated queries across multiple fields. The assessment specifies SQL Server as the preferred database (matching the OneHub production stack on Azure SQL) while allowing PostgreSQL or SQLite for local development. The schema must be version-controlled and managed via migrations.

Domain entities must remain framework-agnostic (no EF Core attributes on `Policy`). All schema mapping must live in the Infrastructure layer.

---

## Decision

Use **SQL Server 2022** (Developer edition in Docker for local development; Azure SQL in production) with **Entity Framework Core 9** as the ORM, configured Code-First with `IEntityTypeConfiguration<T>` classes. All migrations are generated exclusively via `dotnet ef migrations add`; no hand-editing of migration files is permitted.

Key configuration choices:
- All entity mapping in `IEntityTypeConfiguration<T>` classes in Infrastructure — zero EF attributes on domain entities
- Enum columns stored as `nvarchar` strings (not `int`) for human-readable schema
- `decimal(18,2)` for all monetary values; `date` for date-only fields; `datetime2` for timestamps
- `AuditSaveChangesInterceptor` sets `CreatedAt` and `UpdatedAt` automatically on every save
- Migrations applied at startup in Development/Integration environments; SQL script deployment in production

---

## Consequences

### Positive

- SQL Server matches the OneHub production stack — no conversion cost when deploying to Azure SQL
- EF Core 9 provides change tracking, LINQ-to-SQL translation, and automatic `decimal`/`DateOnly` mapping with minimal boilerplate
- `IEntityTypeConfiguration<T>` keeps all mapping concerns in Infrastructure; domain entities have no framework dependency
- Migrations are version-controlled and reversible; the `Down()` method is always implemented
- `AuditSaveChangesInterceptor` centralises audit timestamp logic — no per-handler boilerplate

### Negative

- SQL Server requires Docker for local development (larger image than SQLite)
- EF Core's LINQ translation has limits; complex aggregation queries (e.g., `GetPolicySummary`) may require raw SQL or `ExecuteScalar` as a fallback
- Migrations must be reviewed manually after generation to catch unintended column drops caused by rename detection failures

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **PostgreSQL** | Valid for local dev but diverges from the OneHub production stack; slight dialect differences (e.g., case-sensitivity, `money` type behaviour) could cause subtle production bugs |
| **SQLite** | Appropriate for unit tests (EF InMemory is preferred there) but lacks SQL Server–specific type support (`datetime2`, `decimal(18,2)` precision) needed to validate migration scripts |
| **Dapper (raw SQL)** | More control over query shape but requires hand-authored SQL for every operation; no change tracking; migrations must be maintained as separate SQL scripts outside of EF tooling |
| **Repository-less (direct DbContext in handlers)** | Violates the Clean Architecture rule that Application handlers must not reference EF Core; makes handlers untestable without a database |
