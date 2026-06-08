# Review Report — Phase 4
**Reviewer:** Reviewer Agent
**Date:** 2026-06-08
**Scope:** Phase 4 — Infrastructure Layer (DbContext, PolicyService, Seeder, DI, Migrations)

---

## CRITICAL findings

### CRIT-01: `SaveChangesAsync` override uses `DateTime.UtcNow` directly — violates ADR-010
- **File:** `src\PolicyManagement.Infrastructure\Data\PolicyDbContext.cs`, line 61
- **Standard:** ADR-010 (TimeProvider Injection); CLAUDE.md §Async and Data Access
- **Issue:** The override stamps `CreatedAt` and `UpdatedAt` with `DateTime.UtcNow` (a static, non-injectable call) rather than using an injected `TimeProvider`. This makes any test that verifies timestamp behaviour depend on wall-clock time, contradicting the testability rationale documented in ADR-010. `TimeProvider` must be injected into `PolicyDbContext` (or passed as a parameter) and `_timeProvider.GetUtcNow().UtcDateTime` used instead. This is a non-negotiable testability and consistency issue: `PolicyService.BulkFlagAsync` correctly uses `_timeProvider.GetUtcNow()`, but the `SaveChanges` path silently bypasses the abstraction for all `Added`/`Modified` entries that go through the change tracker.

---

## MAJOR findings

### MAJ-01: `SaveChangesAsync` override will overwrite seeder-supplied `CreatedAt` / `UpdatedAt`
- **File:** `src\PolicyManagement.Infrastructure\Data\PolicyDbContext.cs`, lines 59-75; `src\PolicyManagement.Infrastructure\Data\Seeding\PolicyDataSeeder.cs`, lines 68-96
- **Standard:** PLAN.md Task 4.1 (timestamps set automatically); PLAN.md Task 4.4 (seeder must produce deterministic, reproducible records)
- **Issue:** `PolicyDataSeeder` explicitly sets `CreatedAt = now` and `UpdatedAt = now` on each `Policy` object before calling `db.Policies.AddRangeAsync(policies)` followed by `db.SaveChangesAsync()`. Because `SaveChangesAsync` unconditionally overwrites `CreatedAt` (and `UpdatedAt`) for every `EntityState.Added` entry, the values set by the seeder are silently discarded and replaced with the current wall-clock time at the moment `SaveChangesAsync` is called. The seeder's assignments are therefore dead code. This is not a functional failure in production (the timestamp will still be set), but it means: (a) the seeder's intent to control its own timestamps is silently ignored, and (b) a future unit test that asserts a specific `CreatedAt` value on seeded records will fail unpredictably. The override should only set `CreatedAt` when the entity has not already been assigned a non-default value (i.e., guard with `entry.Entity.CreatedAt == default`), or the seeder should rely entirely on the override and not set timestamps itself.

### MAJ-02: `Program.cs` migration + seed block has no error handling — startup failures are unhandled
- **File:** `src\PolicyManagement.API\Program.cs`, lines 28-33
- **Standard:** CLAUDE.md §Code Quality; PLAN.md Task 4.2 (migration must be idempotent and re-runnable); production risk
- **Issue:** The migration and seed block (`MigrateAsync` + `SeedAsync`) executes outside any `try/catch`. If the database is unreachable at startup (e.g., SQL Server container not yet healthy, network partition), the unhandled exception will crash the API process with no meaningful error message and no opportunity for retry logic or graceful degradation. The block should be wrapped in a `try/catch` with structured logging (or at minimum `Console.Error.WriteLine`) so the cause of the startup failure is visible in container logs. Without this, operators see only a process exit, not the underlying database connectivity error.

### MAJ-03: `PolicyDbContextFactory` hardcodes a connection string — CLAUDE.md security rule violation
- **File:** `src\PolicyManagement.Infrastructure\Data\PolicyDbContextFactory.cs`, line 11
- **Standard:** CLAUDE.md §Security ("Never hardcode secrets, passwords, API keys, or connection strings in code")
- **Issue:** The factory embeds `"Server=localhost;Database=PolicyManagement;Trusted_Connection=True;TrustServerCertificate=True;"` as a string literal. While this class exists solely for design-time use (`dotnet ef migrations add`), the CLAUDE.md prohibition is unconditional: connection strings must not appear in committed source code regardless of the usage context. The risk is low in this specific case (the string uses Windows Integrated Security with no password), but the constraint is stated as non-negotiable. The factory should instead read the connection string from environment variables or a local configuration file (e.g., `appsettings.Development.json` via `ConfigurationBuilder`) and fall back to a documented default only when no configuration is found. A comment clearly marking this as design-time-only does not resolve the violation. The ADR or a comment in the file should also document why `TrustServerCertificate=True` is acceptable in this context.

### MAJ-04: `GetSummaryAsync` — `StatusCounts` GroupBy calls `.ToString()` inside LINQ projection — may not translate to SQL
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`, lines 117-125
- **Standard:** CLAUDE.md §Async and Data Access ("Use IQueryable composition for database filtering; defer execution"); production correctness
- **Issue:** The projection `.Select(g => new { Status = g.Key.ToString(), ... })` calls `.ToString()` on an enum (`PolicyStatus`) inside a server-side LINQ expression. Because enums are stored as strings (ADR-005), EF Core translates the enum column to a `nvarchar`; however, calling `.ToString()` on an enum inside a `Select` over a `GroupBy` key may cause EF Core to evaluate that portion client-side rather than push it to SQL, depending on the EF Core version and provider. The same concern applies to the `premiumByLob` query at line 122-125. The safer and idiomatic approach is to project `g.Key` as the enum value and let EF Core materialise it as a string (since the column is stored as `nvarchar`), or cast to the underlying string representation in a way EF Core can translate. This should be verified against EF Core query logs; if client-side evaluation is silently occurring, the GroupBy result is being materialised into memory before the dictionary is built, undermining the server-side aggregation requirement.

---

## MINOR findings

### MIN-01: `PolicyService.ListAsync` — `SortSelectors` keys use camelCase but ADR-009 specifies lowercase keys
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`, lines 19-30
- **Standard:** ADR-009 (allow-list keys documented as lowercase, e.g., `"policynumber"`, `"premiumamount"`)
- **Issue:** The dictionary is constructed with `StringComparer.OrdinalIgnoreCase`, which means the case-insensitive lookup will work regardless. However, the keys as written (`"policyNumber"`, `"policyholderName"`, `"premiumAmount"`) are camelCase, while ADR-009 shows them as all-lowercase. This is a documentation/consistency discrepancy, not a runtime defect. The `StringComparer.OrdinalIgnoreCase` mitigation makes this safe, but the keys should match the documented ADR so the allow-list serves as the single source of truth shared with the validator without confusion.

### MIN-02: `PolicyDataSeeder` uses `DateTime.UtcNow` on line 68 — bypasses `TimeProvider`
- **File:** `src\PolicyManagement.Infrastructure\Data\Seeding\PolicyDataSeeder.cs`, line 68
- **Standard:** ADR-010 (TimeProvider Injection); CLAUDE.md §Async and Data Access
- **Issue:** `var now = DateTime.UtcNow;` is used to set `CreatedAt` and `UpdatedAt` on seeded records. While the seeder is not a testable service class with an injected `TimeProvider`, this direct call means seeded timestamps will differ between runs and between environments. For a deterministic seed (RISK-09), the timestamp values do not need to be the current time; they could be a fixed date (e.g., `new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`) to ensure every fresh database has identical `CreatedAt`/`UpdatedAt` values. This is also partially moot given MAJ-01: the `SaveChangesAsync` override currently overwrites these values anyway. Once MAJ-01 is resolved by guarding the override, this becomes independently meaningful.

### MIN-03: `PolicyDataSeeder` does not cover all 8 required regions per requirements
- **File:** `src\PolicyManagement.Infrastructure\Data\Seeding\PolicyDataSeeder.cs`, lines 34-38
- **Standard:** `.specs/requirements.md` §Policy Data Schema (regions: Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines); PLAN.md Task 4.4 DoD ("all eight regions are represented")
- **Issue:** The `Regions` array contains `"South Korea"` instead of `"Malaysia"`. The requirements list the eight valid regions as: Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines. `"South Korea"` is not among them and `"Malaysia"` is missing. The `Regions` array also uses `"Hong Kong"` and `"Japan"` correctly, but the substitution of South Korea for Malaysia means the seeded data fails the DoD check on region coverage, and any region-based filtering test for `"Malaysia"` will return zero results.

### MIN-04: `Program.cs` migration + seed block — potential scope drift from Phase plan
- **File:** `src\PolicyManagement.API\Program.cs`, lines 28-33
- **Standard:** PLAN.md Phase 4 (Tasks 4.2–4.4 cover migration and seed); PLAN.md Phase 5 (Task 5.2 covers Program.cs wiring)
- **Issue:** PLAN.md places migration application and seeding as Phase 4/5 infrastructure work but does not explicitly assign the startup invocation (`MigrateAsync` + `SeedAsync` in `Program.cs`) to a specific task. The block is present in `Program.cs` ahead of Phase 5 controller work. This is appropriate — migration-at-startup is standard practice for this architecture — and is not a defect, but the reviewer flags it as a scope note: Phase 5 work on `Program.cs` (health checks, global error handling, Serilog) has not been completed, leaving `TODO` comments in production code paths. The migration/seed block itself is correctly placed but the surrounding incomplete state of `Program.cs` should be resolved in Phase 5 before the file is considered production-ready.

### MIN-05: `PolicyService.ListAsync` — no fallback guard if `query.Sort` maps to an unknown key
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`, lines 71-75
- **Standard:** ADR-009; PLAN.md Task 5.3 (invalid sort fields return 400 before reaching service)
- **Issue:** The service silently falls back to `"createdAt"` when the sort key is not found in `SortSelectors` (via `GetValueOrDefault`). The plan and ADR-009 state that invalid sort fields should be rejected with a 400 response at the validation layer before reaching `PolicyService`. The fallback in the service is therefore a defensive measure that masks validator failures — if the validator is absent or misconfigured, the service silently changes the sort order without informing the caller. This is acceptable as a belt-and-braces default, but the double null-coalescing (`?? SortSelectors["createdAt"]`) is redundant since `GetValueOrDefault` already returns `null` (not the sentinel value) for unknown keys, and the subsequent `??` handles that. Minor style concern only; the logic is correct.

### MIN-06: `PolicyDbContext` missing `SaveChanges` (non-async) override
- **File:** `src\PolicyManagement.Infrastructure\Data\PolicyDbContext.cs`, lines 59-75
- **Standard:** CLAUDE.md §Code Quality; general EF Core best practice
- **Issue:** Only `SaveChangesAsync` is overridden to stamp timestamps. The synchronous `SaveChanges` is not overridden. If any code path (including EF Core internals or third-party tooling) calls the synchronous `SaveChanges`, timestamps will not be set. While CLAUDE.md mandates async I/O for all application code, the override should be complete to prevent silent failures. At minimum, `SaveChanges` should be overridden to throw `NotSupportedException` ("Use SaveChangesAsync") to enforce the async requirement explicitly.

---

## OBSERVATIONS

### OBS-01: `PolicyDbContextFactory` is correctly scoped to design-time
- **File:** `src\PolicyManagement.Infrastructure\Data\PolicyDbContextFactory.cs`
- **Standard:** EF Core design-time factory pattern; PLAN.md Task 4.2
- **Issue (observation only):** The factory implements `IDesignTimeDbContextFactory<PolicyDbContext>`, which is the correct EF Core pattern for enabling `dotnet ef` tooling without a running application host. It will never be instantiated at runtime. This is the correct approach and is well-understood as design-time-only. The hardcoded connection string concern is captured separately in MAJ-03.

### OBS-02: `ListAsync` `IQueryable` chain is correctly deferred
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`, lines 41-87
- **Standard:** CLAUDE.md §Async and Data Access; PLAN.md Task 4.3 DoD
- **Issue (observation only):** All filter, sort, and pagination steps compose against `IQueryable<Policy>` without any intermediate materialisation. `CountAsync` executes first (line 69) against the filtered-but-unpaged query, then `Skip`/`Take`/`Select`/`ToListAsync` execute as a single terminal operation. This is the correct two-query pattern and satisfies the plan's DoD. No `ToList` or `ToListAsync` is called before the final terminal operation.

### OBS-03: `BulkFlagAsync` correctly uses `ExecuteUpdateAsync` with `TimeProvider` and `CancellationToken`
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`, lines 102-113
- **Standard:** ADR-006; ADR-010; PLAN.md Task 4.3 (BulkFlagAsync DoD)
- **Issue (observation only):** `ExecuteUpdateAsync` is used (not load-then-SaveChanges). Both `FlaggedForReview` and `UpdatedAt` are set in one statement. `CancellationToken` is passed. `_timeProvider.GetUtcNow().UtcDateTime` is used (not `DateTime.UtcNow`). `NotFound` is computed as `ids.Count - flagged` (RISK-01 resolution). All ADR-006 and ADR-010 requirements are satisfied in this method.

### OBS-04: `GetSummaryAsync` — expiring-soon filter correctly includes `Status == Active`, uses `TimeProvider`, and reads threshold from `IConfiguration`
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`, lines 127-132
- **Standard:** PLAN.md Task 4.3 (GetSummaryAsync DoD); ADR-010; RISK-02
- **Issue (observation only):** The `CountAsync` predicate at line 130 filters `Status == PolicyStatus.Active`, `ExpiryDate >= now`, and `ExpiryDate <= cutoff`. This is pushed to SQL as a `WHERE` clause (correct — no in-memory filtering). `_timeProvider.GetUtcNow().UtcDateTime` is used. `_expiringSoonDays` is read from `IConfiguration` with a default of 30 (RISK-02 resolution). All required checks pass. The `ExpiryDate >= now` guard correctly excludes already-expired policies from the expiring-soon count.

### OBS-05: `PolicyDataSeeder` — determinism, idempotency, count, enum coverage
- **File:** `src\PolicyManagement.Infrastructure\Data\Seeding\PolicyDataSeeder.cs`
- **Standard:** PLAN.md Task 4.4; RISK-09
- **Issue (observation only):** `new Random(42)` is used (RISK-09 satisfied). `AnyAsync` idempotency check is present at line 59. Record count is 250 (exceeds the 200+ requirement). All 4 statuses are covered (round-robin at line 82). All 4 LOBs are covered (round-robin at line 81). All 6 currencies are sourced from `PolicyManagement.Domain.Constants.Currency` (line 41-44). All DB calls are async (`AnyAsync`, `AddRangeAsync`, `SaveChangesAsync`). The region issue (South Korea vs Malaysia) is filed separately as MIN-03.

### OBS-06: Migration correctly reflects all schema requirements
- **File:** `src\PolicyManagement.Infrastructure\Migrations\20260608182240_InitialCreate.cs`
- **Standard:** PLAN.md Task 4.2; ADR-005; requirements schema
- **Issue (observation only):** `LineOfBusiness` and `Status` are `nvarchar(50)` (string storage — ADR-005 satisfied). `PremiumAmount` is `decimal(18,2)` (precision correct). Unique index on `PolicyNumber` is present. `Down()` drops the table cleanly. All 14 schema fields are present with correct types.

### OBS-07: `Task.Run` — not found anywhere in Phase 4 files
- **File:** All reviewed files
- **Standard:** CLAUDE.md §Async ("Never wrap synchronous work in Task.Run to fake async")
- **Issue (observation only):** No `Task.Run` usage was found in any of the reviewed files. The non-negotiable constraint is satisfied.

### OBS-08: `CancellationToken` threading through all public async methods
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`
- **Standard:** CLAUDE.md §Async and Data Access; PLAN.md Task 4.3
- **Issue (observation only):** All four public methods on `PolicyService` accept `CancellationToken ct` and pass it to every EF Core async call (`CountAsync(ct)`, `ToListAsync(ct)`, `FirstOrDefaultAsync(..., ct)`, `ExecuteUpdateAsync(..., ct)`, `ToDictionaryAsync(..., ct)`, `CountAsync(..., ct)`). Full compliance.

### OBS-09: Method length compliance — all methods within ~40-line limit
- **File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs`
- **Standard:** CLAUDE.md §Code Quality ("No method longer than ~40 lines")
- **Issue (observation only):** `ListAsync` spans lines 39-88 (approximately 49 lines including blank lines and braces, approximately 40 lines of substantive code). This is borderline. `GetSummaryAsync` (lines 115-135, ~20 lines) and other methods are well within the limit. `ListAsync` is at the edge; extracting the search predicate or the sort application into a private helper would bring it clearly under the limit and improve readability.

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 1 |
| MAJOR | 4 |
| MINOR | 6 |
| OBSERVATION | 9 |

## Verdict

REQUEST CHANGES

## Recommendation

Phase 4 demonstrates solid architectural judgment overall: the `IQueryable` composition pattern is correctly deferred, `ExecuteUpdateAsync` is used as specified, the sort allow-list is properly typed, `TimeProvider` is injected and used correctly in `PolicyService`, and the migration matches the schema exactly. However, three issues block approval.

The CRITICAL issue (CRIT-01) and MAJ-01 are related: `PolicyDbContext.SaveChangesAsync` uses `DateTime.UtcNow` instead of an injected `TimeProvider`, which both violates ADR-010 and silently overwrites the timestamps explicitly set by the seeder, making seeder timestamp assignments dead code. These two issues should be resolved together by injecting `TimeProvider` into `PolicyDbContext` and guarding `CreatedAt` stamping with `entry.Entity.CreatedAt == default` so the seeder can control its own values. MAJ-03 (hardcoded connection string in the factory) must be resolved to satisfy the non-negotiable CLAUDE.md security rule, even though the runtime risk is low. MIN-03 (South Korea substituted for Malaysia in the region array) causes the seeder to fail its own DoD and should be fixed to `"Malaysia"` to ensure all eight required regions are represented. With these four issues addressed, the implementation would be in a strong position to proceed to Phase 5.
