# ADR-006: ExecuteUpdateAsync for Bulk Flag Operation

**Status:** Accepted
**Date:** 2026-06-08

## Context

The `PATCH /api/v1/policies/flag` endpoint receives an array of policy IDs and must set `FlaggedForReview = true` and update `UpdatedAt` for each matching policy. There are at least three EF Core approaches to implement this:

1. Load matching entities into memory, mutate them, then call `SaveChangesAsync`.
2. Use `ExecuteUpdateAsync` (introduced in EF Core 7) to issue a single `UPDATE` SQL statement without loading entities.
3. Use raw SQL via `ExecuteSqlRawAsync`.

`CLAUDE.md` mandates: "Always use native async methods for I/O", "Use IQueryable composition for database filtering", and "Use parameterized queries only; never string-concatenate SQL." The plan identifies the no-load approach as the correct implementation under RISK-01.

## Decision

`BulkFlagAsync` in `PolicyService` uses `ExecuteUpdateAsync` on a filtered `IQueryable<Policy>`. The query filters by `Id IN (ids)` and sets `FlaggedForReview = true` and `UpdatedAt = DateTime.UtcNow` in a single SQL `UPDATE` statement.

```csharp
var flaggedCount = await _context.Policies
    .Where(p => ids.Contains(p.Id))
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.FlaggedForReview, true)
        .SetProperty(p => p.UpdatedAt, _timeProvider.GetUtcNow().UtcDateTime),
    cancellationToken);
```

The count of IDs not found is derived as `ids.Count - flaggedCount` (valid because `ExecuteUpdateAsync` returns the number of rows affected). The operation is idempotent: re-flagging an already-flagged policy succeeds silently and the row is counted as flagged.

## Consequences

### Positive
- A single SQL `UPDATE ... WHERE Id IN (...)` statement is issued regardless of how many IDs are in the request, avoiding N+1 database round-trips.
- No entities are loaded into memory; the `DbContext` change tracker is not involved, reducing memory pressure for large batches.
- The operation is inherently idempotent: calling it twice with the same IDs produces the same database state.
- `ExecuteUpdateAsync` uses EF Core's parameterised query infrastructure; the ID list is passed as a parameter, not string-concatenated, satisfying the CLAUDE.md security constraint.
- Native async (`ExecuteUpdateAsync`) satisfies the CLAUDE.md async requirement without wrapping synchronous work in `Task.Run`.

### Negative / Trade-offs
- `ExecuteUpdateAsync` bypasses the EF Core change tracker. Any cached entities in the current `DbContext` instance will not reflect the update until reloaded. In this service, `PolicyDbContext` is scoped per HTTP request, so this is not a practical problem — no caller reads the same entities in the same request after calling `BulkFlagAsync`.
- `ExecuteUpdateAsync` was introduced in EF Core 7. The project targets EF Core 8 (.NET 8), so this is not a version constraint, but it is worth noting for teams on older EF Core versions.
- The "not found" count is computed as `total requested - rows affected`. This is correct for the idempotent model (unknown IDs do not affect the count), but it means an unknown ID and an already-flagged ID are indistinguishable in the response. This is the documented RISK-01 resolution and is acceptable.

## Alternatives Considered

### Alternative: Load Entities Then SaveChanges
**Rejected because:** Loading N entities into memory to set one boolean property is an N+1-style anti-pattern. For a batch of 100 IDs, this is 100 entity objects loaded, mutated, and tracked — all for a single column update. The change tracker overhead, memory allocation, and multiple database round-trips are unnecessary when `ExecuteUpdateAsync` expresses the same intent as a single SQL statement.

### Alternative: Raw SQL String via ExecuteSqlRawAsync
**Rejected because:** `ExecuteSqlRawAsync` with a string containing `IN (...)` requires constructing the ID list as a SQL string fragment, which violates the CLAUDE.md rule against string-concatenating SQL (RISK-04 analogue for write operations). While EF Core's `ExecuteSqlRawAsync` does accept `SqlParameter` objects, the resulting code is more verbose and less type-safe than `ExecuteUpdateAsync` with typed property setters.
