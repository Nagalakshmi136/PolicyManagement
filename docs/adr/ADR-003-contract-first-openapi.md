# ADR-003: Contract-First API Design with OpenAPI 3.x

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The assessment explicitly requires a contract-first approach: "OpenAPI 3.x (Swagger) specification defined before implementing endpoints" and "the contract should be the single source of truth for the API shape". The front-end team building the Policy Overview Dashboard needs a stable, reviewable contract before implementation starts so they can build against it in parallel.

Without a contract-first discipline, the API shape tends to be driven by whatever is convenient to implement, creating mismatches with client expectations and making the spec a post-hoc documentation artifact rather than a specification.

---

## Decision

Author the **OpenAPI 3.x specification first** at `docs/openapi/policy-management.yaml` before any C# implementation. The spec is the single source of truth: no field, endpoint, or error shape may exist in the implementation that is not described in it.

Workflow:
1. Define/update the OpenAPI spec
2. Review the contract (no code yet)
3. Implement controllers, DTOs, and validators to match the spec exactly
4. Validate the running API against the spec via integration tests

Structural rules:
- All schema objects defined under `components/schemas` and referenced via `$ref` — never inlined
- Every path operation has `operationId` (unique, camelCase), `summary`, `tags`, `parameters`, and all expected response status codes
- `record` types for all request/response contracts in `PolicyManagement.Api/Contracts/`
- `DateOnly` for `format: date` fields; `DateTime` for `format: date-time`; `decimal` for monetary values
- API contract records in the Api layer are distinct from Application DTOs — mapped in the controller, never shared

---

## Consequences

### Positive

- Front-end team can build against a stable, reviewed contract before backend implementation
- Contract violations are caught during implementation rather than at integration time
- Spec doubles as living documentation served via Swagger UI / Scalar
- `operationId` consistency enables client SDK generation if needed

### Negative

- Spec must be kept in sync with implementation — drift between spec and code is a risk without automated validation
- Two sets of types (Api contracts + Application DTOs) add a mapping step in every controller action
- Authoring OpenAPI YAML by hand is verbose; inlining errors are easy to introduce

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Code-first with Swashbuckle generating the spec** | Spec becomes a reflection artifact, not a specification; front-end team cannot review it before implementation; `[ProducesResponseType]` attributes are a poor substitute for a formal spec |
| **No spec (implicit contract from controller signatures)** | No formal contract exists; breaking changes are invisible until client integration; the assessment explicitly requires a spec |
| **NSwag / Kiota to generate C# stubs from the spec** | Valid toolchain enhancement but adds build-pipeline complexity; for a 2–3 hour assessment, hand-implemented controllers matching the spec are more practical and equally spec-compliant |
