# ADR-007: EF Core InMemory Provider for Unit Tests

**Status:** Accepted
**Date:** 2026-06-08

## Context

The test project (`PolicyManagement.UnitTests`) must test `PolicyService` methods (list, get by ID, bulk flag, summary) and the validation/middleware behaviour of the API. Tests must be fast, independent, and isolated. There are three practical choices for the database layer in tests:

1. **EF Core InMemory provider** (`Microsoft.EntityFrameworkCore.InMemory`) — in-process, no external dependencies.
2. **SQLite in-memory** (`Microsoft.EntityFrameworkCore.Sqlite` with `DataSource=:memory:`) — runs a real SQLite engine in-process, enforces some constraints.
3. **Testcontainers + SQL Server** (`Testcontainers.MsSql`) — starts a real SQL Server 2022 Docker container per test run, full production fidelity.

`CLAUDE.md` requires tests to be independent, isolated, and fast. The plan (Task 6.1) states "one test project using EF Core InMemory provider — no Testcontainers." RISK-05 in the plan explicitly documents the limitations of the InMemory provider.

## Decision

`PolicyManagement.UnitTests` uses the EF Core InMemory provider for all `PolicyService` tests. Each test method creates a fresh in-memory database with a unique name (`Guid.NewGuid().ToString()`) to prevent state leakage between tests.

```csharp
var options = new DbContextOptionsBuilder<PolicyDbContext>()
    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
    .Options;
using var context = new PolicyDbContext(options);
```

`WebApplicationFactory<Program>` tests replace the SQL Server provider with InMemory via `ConfigureTestServices` for middleware and validation tests.

**Known limitation (documented):** The EF Core InMemory provider does not enforce SQL Server constraints (unique indexes, check constraints, foreign keys, decimal precision). Tests that pass against InMemory may fail against a real SQL Server if they rely on constraint violations or SQL-specific behaviour. This limitation is accepted for this sprint and documented for future resolution.

## Consequences

### Positive
- Tests run entirely in-process with no Docker dependency; the test suite runs in under 5 seconds on any developer machine.
- No container startup latency; each test can use a fresh database instance at negligible cost.
- The InMemory provider is sufficient for testing all business logic in `PolicyService`: filtering, sorting, pagination, `BulkFlagAsync` return counts, and `GetSummaryAsync` aggregations work correctly against in-memory data.
- CI pipelines do not require Docker or a SQL Server instance; tests pass on any build agent with the .NET 8 SDK.

### Negative / Trade-offs
- **The InMemory provider does not enforce unique indexes.** The `PolicyNumber` uniqueness constraint defined in `PolicyConfiguration` is not enforced in tests; duplicate policy numbers can be inserted without error. Business logic tests that depend on uniqueness violations will give false positives.
- **The InMemory provider does not enforce decimal precision.** `PremiumAmount decimal(18,2)` is stored with full CLR decimal precision in memory; tests will not detect truncation or rounding that SQL Server would apply.
- **`ExecuteUpdateAsync` behaviour differs.** The InMemory provider's implementation of `ExecuteUpdateAsync` may not fully replicate SQL Server behaviour for all edge cases; this is mitigated by the simplicity of the bulk-flag operation.
- **SQL-specific functions are not available.** Any EF Core LINQ expression that translates to a SQL Server-specific function (e.g., `LIKE` pattern matching edge cases, `GETUTCDATE()`) may behave differently in InMemory.
- Production readiness requires replacing InMemory with Testcontainers-backed SQL Server integration tests (see "What I Would Do Next" in the plan).

## Alternatives Considered

### Alternative: Testcontainers + Real SQL Server 2022
**Rejected because:** Testcontainers starts a real SQL Server 2022 Docker container for each test run, providing full production fidelity. This is the correct choice for integration tests. However, it requires Docker to be running on every CI agent, adds 10–30 seconds of container startup per test run, and is inappropriate for fast unit tests. The plan deliberately separates "unit tests" (InMemory, this sprint) from "integration tests" (Testcontainers, a future sprint) and names this as the natural next investment. Using Testcontainers for all tests would blur this distinction and slow the feedback loop for business logic changes.

### Alternative: SQLite In-Memory
**Rejected because:** SQLite enforces more constraints than the EF Core InMemory provider (e.g., some foreign key checks, basic type coercions), making tests marginally more realistic. However, SQLite still does not enforce SQL Server-specific behaviour: SQL Server `decimal(18,2)` precision, `nvarchar` vs `varchar`, SQL Server `LIKE` collation rules, and `ExecuteUpdateAsync` translation all differ. The improvement over InMemory is incremental but the additional package dependency and subtle dialect differences introduce their own confusion. Given that the long-term path is Testcontainers (full production fidelity), SQLite is an intermediate step that adds complexity without a clear benefit over InMemory for this sprint scope.
