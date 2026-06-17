# Policy Management BFF

Backend-for-Frontend REST API for insurance policy management, built as part of a Chubb APAC take-home assessment.

**Stack:** .NET 10 · ASP.NET Core Web API · EF Core 9 · SQL Server · MediatR · FluentValidation · Serilog · Keycloak 24+ · xUnit · Docker

---

## Quick Start

```bash
# Run full stack (API + SQL Server + Keycloak)
cp .env.example .env   # fill in values
docker-compose up --build

# API:      http://localhost:5000/api/v1/policies
# Keycloak: http://localhost:8180
# Health:   http://localhost:5000/health/ready
```

```bash
# Run tests
dotnet test

# Build only
dotnet restore && dotnet build

# Add a migration
dotnet ef migrations add <Name> \
  --project src/PolicyManagement.Infrastructure \
  --startup-project src/PolicyManagement.Api \
  --output-dir Persistence/Migrations
```

---

## Repository Layout

```
src/
├── PolicyManagement.Api/            # Controllers, middleware, DI wiring
├── PolicyManagement.Application/    # CQRS handlers, validators, DTOs
├── PolicyManagement.Domain/         # Entities, enums, domain events, repository interfaces
└── PolicyManagement.Infrastructure/ # EF Core, repositories, migrations, seed data

tests/
├── PolicyManagement.Domain.Tests/
├── PolicyManagement.Application.Tests/
├── PolicyManagement.Infrastructure.Tests/
└── PolicyManagement.Api.Tests/      # Integration tests via WebApplicationFactory

docs/
├── architecture.md
├── analysis/policy-management-bff-analysis.md
├── openapi/policy-management.yaml   # OpenAPI 3.1 spec — source of truth
└── adr/                             # Architecture Decision Records

keycloak/
└── realm-export.json                # Pre-configured realm, clients, roles, seed users

.github/
├── agents/                          # Custom Copilot agents (see below)
├── skills/                          # Coding standards referenced by agents
└── copilot-instructions.md
```

---

## Copilot Agents

| Agent | File | Invoke when |
|-------|------|-------------|
| **Product Analyst** | `product-analyst.agent.md` | Analysing requirements, writing acceptance criteria, identifying NFRs and risks |
| **Architect** | `architect.agent.md` | Designing layer structure, database schema, ADRs, dependency rules |
| **OpenAPI Designer** | `openapi-designer.agent.md` | Designing or updating the OpenAPI spec, defining schemas, adding security |
| **Backend Developer** | `backend-developer.agent.md` | Implementing features — handlers, repositories, controllers, DTOs |
| **QA Engineer** | `qa-engineer.agent.md` | Writing unit tests, integration tests, identifying edge cases |
| **DevOps Engineer** | `devops-engineer.agent.md` | Dockerfile, docker-compose, GitHub Actions, health checks |
| **Reviewer** | `reviewer.agent.md` | Reviewing code for architecture, security, performance, test coverage |
| **Commit Writer** | `commit-writer.agent.md` | Generating Conventional Commit messages for staged changes |
| **PR Writer** | `pr-writer.agent.md` | Generating pull request descriptions from branch commits |

All agents read the relevant skill files in `.github/skills/` before performing any task.

---

## Architecture

Clean Architecture (Onion) — dependencies point inward only.

```
Api → Application → Domain
Infrastructure → Domain
Infrastructure → Application  (interface contracts only)
Api → Infrastructure           (Program.cs DI registration only)
```

Domain has zero NuGet dependencies. Application never references `Microsoft.AspNetCore.*` or EF Core.

See `docs/architecture.md` and `.github/copilot-instructions.md` for full conventions.
