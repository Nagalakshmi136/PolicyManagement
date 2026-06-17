# ADR-005: URL Path Versioning for the API

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The BFF API must be versioned so that breaking changes can be introduced without immediately forcing all clients to update. The three common .NET API versioning strategies are:

1. **URL path** — `/api/v1/policies`, `/api/v2/policies`
2. **Query string** — `/api/policies?api-version=1.0`
3. **HTTP header** — `Api-Version: 1.0`

The choice affects cacheability, explicitness in logs/metrics, client convenience, and OpenAPI spec organisation.

---

## Decision

Use **URL path versioning** (`/api/v{version}/`) as the sole versioning strategy.

Configuration:
- Default version: `1.0`
- `AssumeDefaultVersionWhenUnspecified: true` — unversioned requests are treated as v1 without error
- `ReportApiVersions: true` — responses include `api-supported-versions` header
- Each major API version has its own OpenAPI spec file: `docs/openapi/v1/policy-management.yaml`
- Minor non-breaking additions increment `info.version` as a minor bump within the same spec file (e.g., `1.1`)

---

## Consequences

### Positive

- Explicit and visible: the version is in the URL, making it obvious from browser dev tools, access logs, and curl commands
- Cache-friendly: `/api/v1/policies` and `/api/v2/policies` are distinct cache keys; CDN and proxy caching work without custom configuration
- Easy to route at the infrastructure level (Nginx, API gateway rules can route by path prefix)
- Swagger UI can surface multiple spec files with clear v1/v2 separation

### Negative

- URLs are "ugly" to REST purists — a resource's URL changes when the version changes, which technically violates REST resource identity principles
- Requires duplicating the version segment in every controller `[Route]` attribute
- Clients must update their base URL when migrating to a new major version (vs. a header they can change silently)

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Query string versioning** (`?api-version=1.0`) | Less visible in logs and developer tools; query parameters are often stripped by intermediate proxies; makes URL bookmarking and curl testing less convenient |
| **HTTP header versioning** (`Api-Version: 1.0`) | Invisible in browser URL bars, logs, and curl without explicit header inspection; requires client SDK or custom HTTP client configuration; not cache-key-friendly |
| **No versioning (single API, always latest)** | Acceptable only for internal services with a single coordinated deployment of client and server; a BFF serving an independent front-end team cannot make that assumption |
