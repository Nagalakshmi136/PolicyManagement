# ADR-006: IMemoryCache for Caching (Bonus)

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The requirements call for a caching layer for frequently accessed data — specifically summary statistics and policy listings — with a clear invalidation strategy. Two cache tiers are available in .NET:

1. **`IMemoryCache`** — in-process, single-instance, low-latency
2. **`IDistributedCache`** (Redis, SQL Server) — cross-process, multi-instance, network-latency

The BFF is designed for a single-instance deployment in this assessment. The summary statistics endpoint is the most expensive query (full-table aggregation) and the most read-heavy. Policy list pages are also good cache candidates given the bounded 200-record dataset.

---

## Decision

Use **`IMemoryCache`** as the initial caching implementation. The cache is accessed within Application layer handlers via an `ICacheService` abstraction (defined in Application, implemented in Infrastructure), so the backing store can be swapped to `IDistributedCache` without changing handler code.

Cache targets and configuration:

| Entry | Key pattern | Expiry type | Duration |
|---|---|---|---|
| Summary stats | `v1:policies:summary` | Sliding | 5 minutes |
| Individual policy | `v1:policy:{id}` | Sliding | 5 minutes |
| Policy list page | `v1:policies:list:{hash}` | Absolute | 1 minute |

Invalidation strategy:
- `FlagPoliciesCommandHandler` evicts `v1:policies:summary`, `v1:policy:{id}` for each flagged ID, and all `v1:policies:list:*` entries after a successful database commit
- Cache keys are centralised in a `CacheKeys` static class — no inline strings in handlers
- Key prefix `v1:` allows global invalidation by prefix if a schema change would invalidate all entries

---

## Consequences

### Positive

- Zero operational overhead — no Redis cluster to provision or monitor for local development or single-instance deployment
- Sub-millisecond cache access; summary query latency drops from a full SQL aggregation to a memory read on cache hit
- `ICacheService` abstraction isolates handlers from the caching implementation; switching to Redis requires only a new `ICacheService` implementation registered in `AddInfrastructure()`

### Negative

- In-process cache is lost on process restart (acceptable for summary stats with a 5-minute TTL)
- If the service scales horizontally, each instance has its own cache — summary stats can be stale on some instances until TTL expires; a distributed cache would be required for consistency
- Cache invalidation of list pages by prefix requires a custom implementation or iterating all keys; `IMemoryCache` does not natively support key-pattern eviction

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Redis via `IDistributedCache`** | Adds Docker Compose service, connection string configuration, and network dependency for a single-instance assessment deployment; the abstraction allows switching to Redis when horizontal scaling is required without handler changes |
| **No caching** | The summary endpoint performs a full-table aggregation on every request; given the read-heavy nature of a dashboard BFF, caching is a meaningful production concern even in the assessment context |
| **Response caching middleware** (`UseResponseCaching`) | Output-level caching; inflexible invalidation (invalidation requires HTTP cache-control semantics, not programmatic eviction); does not integrate with business-event-driven invalidation |
