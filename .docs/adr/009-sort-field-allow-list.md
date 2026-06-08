# ADR-009: Allow-List for Dynamic Sort Field (Security — RISK-04)

**Status:** Accepted
**Date:** 2026-06-08

## Context

The list endpoint (`GET /api/v1/policies`) accepts a `sort` query parameter specifying a column name and direction (e.g., `premiumAmount,desc`). The sort column must be applied dynamically to an EF Core `IQueryable<Policy>` at runtime. The user-supplied column name string must be translated to a valid `OrderBy` expression.

The naive implementation passes the column name string directly to a dynamic `OrderBy` call (e.g., via System.Linq.Dynamic or string interpolation into a raw SQL clause). This creates a SQL injection risk: an attacker could supply a sort value containing SQL fragments (e.g., `; DROP TABLE Policies --`) and, depending on implementation, achieve arbitrary SQL execution.

`CLAUDE.md` mandates: "Use parameterized queries only; never string-concatenate SQL." RISK-04 in the plan identifies this surface explicitly and prescribes the allow-list approach.

## Decision

Sorting is implemented using a compile-time allow-list: a `Dictionary<string, Expression<Func<Policy, object>>>` mapping each permitted sort field name (lowercase, as it appears in the API parameter) to a typed lambda selector.

```csharp
private static readonly Dictionary<string, Expression<Func<Policy, object>>> SortSelectors = new()
{
    ["policynumber"]       = p => p.PolicyNumber,
    ["policyholdername"]   = p => p.PolicyholderName,
    ["premiumamount"]      = p => p.PremiumAmount,
    ["effectivedate"]      = p => p.EffectiveDate,
    ["expirydate"]         = p => p.ExpiryDate,
    ["status"]             = p => p.Status,
    ["lineofbusiness"]     = p => p.LineOfBusiness,
    ["region"]             = p => p.Region,
    ["createdat"]          = p => p.CreatedAt,
};
```

If the supplied sort field does not exist in the dictionary (case-insensitive lookup), the request is rejected with a 400 Problem Details response before reaching the database. The same dictionary keys are used in request validation (`PolicyListQueryValidator`) so the allow-list is defined once and reused.

The `Expression<Func<Policy, object>>` selectors are passed directly to EF Core's `OrderBy`/`OrderByDescending` methods, which translate them to typed SQL `ORDER BY` clauses. No string interpolation into SQL occurs.

## Consequences

### Positive
- SQL injection via the sort parameter is structurally impossible: the only values that reach EF Core are typed lambda expressions defined in source code, never user-supplied strings.
- The allow-list is a single source of truth used by both validation (400 response for unknown fields) and query building (correct `ORDER BY` translation). Adding a new sortable field requires one entry in one place.
- Invalid sort fields are rejected at the validation layer before any database interaction, keeping `PolicyService` free from validation logic.
- The allow-list is visible in code and reviewable; there is no magic reflection or dynamic compilation that could inadvertently expose columns.

### Negative / Trade-offs
- Every new sortable field requires a manual entry in the dictionary. This is a minor maintenance overhead and is intentional — adding a sort field is a deliberate decision, not automatic.
- The dictionary keys are lowercase strings; the comparison must be case-insensitive. If the normalization logic is applied inconsistently between validation and query building, a valid field could pass validation but fail to find a selector. This is mitigated by using the same dictionary for both operations.

## Alternatives Considered

### Alternative: Raw String Interpolation into SQL
**Rejected because:** Passing the user-supplied sort column name directly into a SQL string (e.g., `$"ORDER BY {sortField} {direction}"`) creates a classic SQL injection vulnerability. An attacker supplying `premiumAmount; DROP TABLE Policies--` as the sort parameter would execute arbitrary SQL. This approach violates the CLAUDE.md security constraint unconditionally and is rejected.

### Alternative: Reflection on Property Names
**Rejected because:** Using reflection to validate that the supplied sort field matches a `Policy` property name (e.g., `typeof(Policy).GetProperty(sortField, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance) != null`) prevents injection of non-column strings but still passes user input (the property name) directly to the query builder. If the query builder uses string-based dynamic LINQ (e.g., `System.Linq.Dynamic.Core`), a valid property name that maps to a navigation property or a shadow property could still cause unintended query behaviour. Reflection also exposes all properties, including internal ones not intended to be sortable. The typed allow-list is strictly safer because only explicitly listed fields are sortable, regardless of what properties the entity has.
