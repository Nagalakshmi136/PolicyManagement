# ADR-007: Partial-Success Semantics for Bulk Flag Endpoint

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

`PATCH /api/v1/policies/flag` accepts an array of policy IDs and sets `flaggedForReview = true` on each. The requirements do not specify what should happen if some IDs exist in the database and others do not.

Two valid approaches exist:

1. **Atomic / all-or-nothing:** If any ID is not found, roll back all changes and return `404` (or `422`)
2. **Partial success:** Flag all IDs that exist; return a response enumerating which IDs were flagged and which were not found

The choice affects the response contract, the database transaction scope, and the client's recovery behaviour.

---

## Decision

Adopt **partial-success semantics**:

- Flag all policy IDs that exist in the database
- Return `200 OK` with a `FlagPoliciesResponse` body containing:
  - `flaggedCount`: number of policies successfully flagged
  - `flaggedIds`: array of IDs that were flagged
  - `notFoundIds`: array of IDs that were not found (empty if all resolved)
- The operation is idempotent: flagging an already-flagged policy is a no-op and the ID appears in `flaggedIds`
- An empty `policyIds` array returns `400 Bad Request` (structural validation, not a business rule)
- If the entire array resolves to zero found policies, the response is still `200 OK` with `flaggedCount: 0` and all IDs in `notFoundIds` — the operation succeeded in the sense that there is nothing erroneous about the request itself

---

## Consequences

### Positive

- Resilient to stale references on the client: a dashboard flagging 50 policies at once should not fail entirely because one policy was deleted moments earlier
- The explicit `notFoundIds` list gives the client actionable information to reconcile its local state
- Idempotent by design — repeated calls with the same ID produce the same result
- Simpler transaction scope: one `UPDATE ... WHERE PolicyId IN (...)` rather than N separate existence checks inside a transaction with rollback logic

### Negative

- `200 OK` with a partial `notFoundIds` list may be unexpected for clients that treat `200` as "all succeeded"; the response contract must be clearly documented in the OpenAPI spec
- A caller cannot distinguish "the request was fully successful" from "partially successful" by HTTP status code alone — they must inspect the response body
- If strict audit requirements emerge (all-or-nothing for regulatory traceability), this decision would need to be revisited

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Atomic / all-or-nothing (`404` if any ID missing)** | Overly strict for a bulk operation; forces the client to pre-validate all IDs before submission; a transient deletion between list and flag would cause the entire bulk operation to fail |
| **Atomic with `207 Multi-Status`** | More precise semantically, but `207` is uncommon in REST APIs outside WebDAV; adds client parsing complexity; not in the project's established error-response vocabulary |
| **Silent ignore (flag found; silently ignore not-found)** | Does not give the client visibility into reconciliation failures; stale IDs accumulate silently |
