# ADR-009: Multi-Currency Premium Grouping and Expiring-Soon Window

- **Status:** Accepted
- **Date:** 2026-06-16
- **Deciders:** Architect

---

## Context

The `GET /api/v1/policies/summary` endpoint must return `totalPremiumByLineOfBusiness` and `expiringSoonCount`. Two ambiguities exist in the requirements:

**Ambiguity 1 — Multi-currency premium aggregation:**
The policy dataset spans six currencies (USD, SGD, HKD, AUD, JPY, THB). The requirements do not specify whether the premium aggregation should:
- Sum raw amounts across all currencies per line of business (produces misleading totals — JPY and USD are not additive)
- Convert all amounts to a single base currency before summing (requires a live or static exchange-rate source not mentioned in the requirements)
- Group by both `lineOfBusiness` and `currency` (returns multiple rows per line of business but each row is accurate)

**Ambiguity 2 — "Expiring soon" threshold:**
The requirements use the phrase "expiring-soon count" without defining the time window. Without a fixed threshold, the metric cannot be tested or agreed upon with the front-end team.

---

## Decision

### Premium aggregation

Group total premium by **both `lineOfBusiness` AND `currency`**. The response shape is an array of objects, not a map:

```json
"totalPremiumByLineOfBusiness": [
  { "lineOfBusiness": "Marine",   "currency": "USD", "totalPremium": 1250000.00 },
  { "lineOfBusiness": "Marine",   "currency": "SGD", "totalPremium":  430000.00 },
  { "lineOfBusiness": "Property", "currency": "USD", "totalPremium": 3100000.00 }
]
```

This approach:
- Produces accurate, non-misleading figures with no external dependency
- Allows the front-end to perform currency conversion or display per-currency breakdowns
- Is documented in the OpenAPI spec description so the front-end team can design their aggregation UI accordingly

### Expiring-soon threshold

Define **30 calendar days from the current UTC date** as the "expiring soon" window. Policies where `ExpiryDate <= today + 30 days AND ExpiryDate >= today` are counted.

The 30-day threshold is:
- Documented in the OpenAPI `description` field of the `expiringSoonCount` property
- Readable from configuration (`Cache:ExpiringSoonDays` with default `30`) so it can be adjusted without a code change
- Documented in the AI working journal as an assumption

---

## Consequences

### Positive

- No exchange-rate service dependency; the summary endpoint has zero external network calls beyond the database query
- Per-currency breakdown is more useful to APAC operations teams who may track premiums by currency for regulatory reporting
- 30-day window aligns with common insurance renewal notification practice; it is a safe default that can be overridden via configuration

### Negative

- The `totalPremiumByLineOfBusiness` response is an array (not a simple map), which is slightly more complex for the front-end to render as a grouped table
- A configurable `ExpiringSoonDays` setting adds one more Options class to maintain
- If the front-end team expected a single aggregated total per line of business in a single currency, this decision produces a different shape — this must be communicated to the front-end team before implementation

---

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Sum raw amounts across currencies (single total per line of business)** | Produces mathematically incorrect results: summing 100,000 USD and 100,000 JPY yields a number that is meaningless without knowing the exchange rate |
| **Convert to USD using a hardcoded rate table** | Hardcoded rates become stale immediately; produces misleading precision; requires maintenance; not mentioned in the requirements |
| **Return null / omit totalPremiumByLineOfBusiness when multi-currency data exists** | Unhelpful to the front-end; the summary endpoint is the primary dashboard aggregate |
| **7-day expiring-soon window** | Too short for APAC insurance renewal workflows; renewals typically require 30+ days lead time |
| **60-day expiring-soon window** | Too broad; a 60-day view would include a large fraction of any active policy book, diluting the signal |
