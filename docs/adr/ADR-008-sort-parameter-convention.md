# ADR-008: Two-Parameter Sort Convention (sortBy + sortDirection)

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The raw requirements specification documents the sort parameter as a single comma-delimited string:

```
sort=premiumAmount,desc
```

The project's contract-first API skill standard documents a two-parameter convention:

```
sortBy=premiumAmount&sortDirection=desc
```

These two formats are mutually incompatible in the OpenAPI spec: the comma-delimited format requires a single `string` parameter with a custom parsing rule; the two-parameter format expresses each concern as an independently typed, documented, and validated parameter.

---

## Decision

Adopt the **two-parameter convention** (`sortBy` + `sortDirection`) for the `GET /api/v1/policies` endpoint.

- `sortBy`: `string`, optional, default `createdAt`; enumerated set of valid field names defined in the OpenAPI spec as an `enum` under `components/parameters`
- `sortDirection`: `string`, optional, default `asc`; enum values `asc` | `desc`

An unrecognised `sortBy` value returns `400 Bad Request` with an `errors.sortBy` validation message. An unrecognised `sortDirection` value returns `400 Bad Request` with an `errors.sortDirection` message.

---

## Consequences

### Positive

- Each parameter is independently typed, documented with an `enum` constraint, and individually validatable in FluentValidation
- OpenAPI tooling (Swagger UI, Spectral lint, client generators) can enumerate valid sort fields without custom parsing logic
- Two parameters are individually optional — a client can specify `sortBy` alone (direction defaults to `asc`) without embedding a comma in a string value
- Aligns with the project's established contract-first API skill standard

### Negative

- Diverges from the comma-delimited format stated in the raw requirements specification
- Clients familiar with the `sort=field,dir` convention (common in Spring Data REST APIs) may need to adapt
- Requires documenting the deviation in the AI working journal per the assessment's documentation requirements

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Comma-delimited `sort=field,dir`** (as in raw requirements) | Cannot be expressed as a typed enum parameter in OpenAPI 3.x; requires a custom `string` parameter with parsing logic in the controller or a `FluentValidation` rule that splits on comma; poor tooling support; inconsistent with the established project convention |
| **`sort` as a `string` with OpenAPI `pattern` constraint** | Pattern-based validation (`^[a-zA-Z]+,(asc\|desc)$`) is error-prone to author and difficult for clients to discover valid field names from |
