# AI Working Journal — Policy Management BFF Service

This journal records what was built each phase, what AI suggested vs. what was accepted, modified, or declined, and the reasoning behind each decision. It is updated at each human gate.

---

## Phase 1 — Foundation

**Date:** 2026-06-08
**ADRs governing this phase:** ADR-001 (Clean Architecture), ADR-004 (SQL Server 2022)

### What was built

- Solution file and five C# projects: `PolicyManagement.Domain`, `PolicyManagement.Application`, `PolicyManagement.Infrastructure`, `PolicyManagement.API`, `PolicyManagement.UnitTests`.
- Project references in `.csproj` files enforce the inward-only dependency rule at compile time: API → Infrastructure → Application → Domain. A forbidden cross-reference fails the build — no runtime or lint check required.
- `docker-compose.yml` with SQL Server 2022 (`mcr.microsoft.com/mssql/server:2022-latest`), a healthcheck that polls `SELECT 1` via `sqlcmd`, and an API service that waits on `service_healthy` before starting.
- `.env` pattern for secrets: `SA_PASSWORD` and `ConnectionStrings__DefaultConnection` are read via `${VARIABLE}` interpolation from a `.env` file that is gitignored. No credential appears in any committed file.
- `appsettings.json` baseline, including `PolicySummary:ExpiringSoonThresholdDays: 30` to resolve RISK-02 from the plan.
- `Program.cs` scaffold with clearly marked `// TODO` placeholders for Serilog, `AddApplication()`, `AddInfrastructure()`, `AddHealthChecks()`, exception middleware, and `MapHealthChecks()`.

### What the human gate caught

**Docker Compose healthcheck receiving an empty password.** The initial implementation placed `SA_PASSWORD` in the service's own `environment:` block (in `docker-compose.override.yml`) rather than in the `.env` file. Docker Compose performs variable interpolation — resolving `${SA_PASSWORD}` in the compose YAML — at parse time from the `.env` file or host environment, not from a sibling service's `environment:` section. The healthcheck command was therefore receiving an empty string for `-P "${SA_PASSWORD}"` and failing. Fixed by moving all credentials to `.env` (gitignored) and removing them from the override file. Verified clean via `docker-compose config` showing the resolved values.

**`version: "3.9"` obsolete attribute.** Both `docker-compose.yml` and `docker-compose.override.yml` included a top-level `version:` key, which Docker Compose V2 treats as obsolete and warns on. Removed from both files before commit.

**`.claude/settings.local.json` staged for commit.** Claude Code's machine-specific permissions file was in the staging area. This file is developer-local and must never land in history. Excluded via `.gitignore` before the commit was made.

### Pros

- Secrets pattern is correct from the first commit. There is no "fix the hardcoded password" commit in the history, which matters for a repository under review.
- Layer dependencies enforced at compile time means no future agent or developer can introduce a forbidden reference without a deliberate `.csproj` edit and a build failure.
- Docker healthcheck is verified working before any application code is written, so Phase 4 can rely on it.

### Cons

- The initial `Program.cs` had no placeholder markers, meaning a reviewer could not verify at a glance which wiring was deferred versus forgotten. The reviewer correctly flagged this (MAJOR-01). Adding explicit `// TODO` comments was a one-line fix but the gap should have been anticipated.

### Takeaway

The `.env` vs. service-environment distinction in Docker Compose is a subtle but important detail. Variable interpolation in compose YAML — `${VARIABLE}` — is resolved at parse time from the `.env` file or host environment. It does not draw from a service's own `environment:` block. A working healthcheck requires the variable to be present in `.env` (or exported in the shell), not just defined for the container that will ultimately consume it.

---

## Phase 2 — Domain Layer

**Date:** 2026-06-08
**ADRs governing this phase:** ADR-001 (Clean Architecture — Domain has no outward references), ADR-005 (enum-as-string — conversion applied in Infrastructure EF config, not here)

### What was built

- `PolicyStatus` enum: `Active`, `Expired`, `Pending`, `Cancelled`.
- `LineOfBusiness` enum: `Property`, `Casualty`, `AandH`, `Marine`.
- `Policy` POCO: 14 auto-properties with public getters and setters. Zero framework attributes (`[Key]`, `[Required]`, etc. are all absent). Zero NuGet references in `PolicyManagement.Domain.csproj`.
- `Currency` static class with six `const string` fields: `USD`, `SGD`, `HKD`, `AUD`, `JPY`, `THB`.

### Non-obvious decisions made

**`AandH` naming.** The requirements use `"A&H"` as the line-of-business label. `&` is not a valid C# identifier character, so the enum member is named `AandH`. The human-readable `"A&H"` label is a serialization concern — it belongs in the API layer's JSON converter configuration, not in the Domain. This is the correct layering: the Domain defines the concept; the API layer owns the wire representation.

**`Currency` as a static class with constants, not an enum.** Currencies are conceptually open-ended — the supported list is a business constraint, not a closed type. Had `Currency` been an enum, adding a new currency (e.g., `CNY`) would require recompilation and a new migration. As `const string` fields, a new value is a one-line addition with no schema impact. The YAGNI argument is also relevant here: no write endpoint exists in this sprint that could insert an invalid currency value. The only writer is the seeder, which references `Currency.USD`, `Currency.SGD`, etc. directly. A database check constraint — which would be the appropriate enforcement mechanism — is deferred to when a write endpoint is introduced (per RISK-08 resolution in the plan).

**String properties initialized to `string.Empty`.** All string properties default to `string.Empty` rather than `null`. This aligns with EF Core's required-by-default convention for non-nullable reference types (enabled by the SDK's nullable context) and avoids null-reference surprises when iterating unseeded records in tests. The trade-off is that a future reader may expect `null` to signal "absent" — but the DTOs in the Application layer can express that distinction independently.

### What the reviewer flagged

**MINOR-04 — `Policy.Id = Guid.Empty` initializer.** The reviewer noted that explicitly assigning `Guid.Empty` to `Id` is redundant (it is the CLR default for `Guid`) and potentially misleading: if a `Policy` object were persisted without setting `Id`, EF Core would insert `00000000-0000-0000-0000-000000000000` rather than throwing or auto-generating a value. This recommendation was **declined** — see the Declined Recommendations log below.

### Pros

- Domain is genuinely infrastructure-free. Verified: `PolicyManagement.Domain.csproj` has zero `<PackageReference>` elements. Phase 6 unit tests can instantiate `Policy` and assert business logic with no test infrastructure overhead.
- `Currency` as constants is more honest about the domain than an enum would be. It does not pretend the set of currencies is closed.
- Enums as C# enums (not strings) in the Domain is correct. The string-storage concern belongs to EF Core configuration in Infrastructure, not to the Domain type definition. This separation is clean.

### Cons

- String properties defaulting to `string.Empty` is a stylistic choice with a real downside: if a future reader extends the model and expects `null` for absent optional fields, there is an inconsistency to resolve. This is a known trade-off, not an oversight.

### Takeaway

Keeping Domain free of all framework references is not just aesthetic. It means Phase 6 unit tests can instantiate `Policy` and run business logic assertions with zero test infrastructure overhead — no DI container, no EF Core setup, no database. This is the primary practical benefit of the Clean Architecture constraint, and it pays off immediately in the test phase.

---

## Phase 1 & 2 — Gate-1 Review and Corrections

**Date:** 2026-06-08
**Review verdict:** APPROVE WITH MINOR
**Reviewer:** Reviewer Agent

### Review scope

6 findings: 0 CRITICAL, 2 MAJOR, 4 MINOR, 4 OBSERVATIONS.

### MAJOR findings addressed

**MAJOR-01 — Program.cs had no DI placeholder markers (fixed).**
`Program.cs` was the verbatim ASP.NET Core scaffold: `AddControllers()`, `AddSwaggerGen()`, nothing else. Without visible placeholders, a downstream implementer could start Phase 5 without noticing that `AddInfrastructure()` and `AddApplication()` were never called — the app would start and serve HTTP with no database wiring and no indication of the gap.

Fixed by adding clearly labelled `// TODO (Phase X):` comments for: Serilog host configuration, `AddApplication()`, `AddInfrastructure(builder.Configuration)`, `AddHealthChecks()`, `UseExceptionHandler()`, `MapHealthChecks()`. These are not implementations — they are mandatory breadcrumbs. Phase 5 cannot be considered complete without them.

**MAJOR-02 — UnitTests project missing API reference and `Microsoft.AspNetCore.Mvc.Testing` (fixed).**
`ARCHITECTURE.md` explicitly states that `PolicyManagement.UnitTests` must reference the API project and include `Microsoft.AspNetCore.Mvc.Testing` to support `WebApplicationFactory<Program>` tests (Task 6.2). Neither was present in the initial `.csproj`. Adding these in Phase 2 costs one `.csproj` edit. Discovering the gap in Phase 6 — mid-sprint, under testing pressure — would require a project-reference edit that could cascade into dependency resolution issues. Fixed immediately.

### MINOR findings addressed

**MINOR-01 — Redundant direct Domain reference in API.csproj (fixed).**
`PolicyManagement.API.csproj` had three project references: Infrastructure, Application, and Domain. `ARCHITECTURE.md` specifies only Infrastructure and Application. Domain is already transitively available via both paths (Infrastructure → Application → Domain). The direct reference was an undocumented deviation that could mislead a future developer into thinking the API is expected to use Domain types directly rather than through Application-layer DTOs. Removed.

**MINOR-03 — `DomainPlaceholder.cs` not deleted (fixed).**
The placeholder class existed to satisfy the compiler before any Domain types were written. Now that `Policy.cs`, `PolicyStatus.cs`, `LineOfBusiness.cs`, and `Currency.cs` all exist, the placeholder adds noise. Deleted.

**MINOR-04 — `Policy.Id = Guid.Empty` initializer (declined).**
See Declined Recommendations log below.

**MINOR-02 — `docker-compose.override.yml` tracking conflict (confirmed non-issue).**
The reviewer observed that `.gitignore` excludes `docker-compose.override.yml` but the file appeared to be tracked. Confirmed via `git ls-files docker-compose.override.yml` — the command returned empty output. The file was never committed to source control. The gitignore rule is correct and functioning. No action taken.

### Observations noted, not acted on

- **OBS-01** — `Infrastructure.csproj` uses floating `8.0.*` version wildcards. Acceptable for an assessment; production use would require pinned versions.
- **OBS-02** — `appsettings.Development.json` contains a `"Description"` key used as an inline comment. Harmless but non-standard; noted for future cleanup.
- **OBS-03** — Healthcheck hardcodes `/opt/mssql-tools18/bin/sqlcmd` path, which is image-version-specific. Low-risk for assessment; a future image update could break it silently.
- **OBS-04** — `Serilog.AspNetCore` is absent from `API.csproj`. Expected — this is Phase 5 (Task 5.6) work. Noted so the Phase 5 implementer knows to add it.

### Pros of the gate process

The two MAJOR findings represent genuine risks that would have compounded if left in place. Missing test infrastructure (MAJOR-02) discovered in Phase 6 is a cascade of `.csproj` changes at the worst possible time. Silent DI wiring gaps (MAJOR-01) are exactly the kind of bug that passes all tests but fails in production. Both were cheap to fix in Phase 2 and expensive to discover later.

### Cons of the gate process

A review at this stage is necessarily forward-looking: several findings describe what Phase 5 and Phase 6 will need, rather than what is wrong right now. This makes some findings feel premature. That is an acceptable trade-off — the alternative is discovering the gaps under deadline pressure in a later phase.

### Takeaway

The reviewer's job at a Phase 1-2 gate is as much about setting up Phase 5-6 safely as it is about catching current defects. A clean domain layer and correct secrets pattern are table stakes; the real value of the gate is verifying that the scaffold will not betray the phases that build on it.

---

## Reviewer Recommendations Declined

| Phase | Finding | Recommendation | Decision | Reasoning |
|---|---|---|---|---|
| Phase 2 | MINOR-04 | Remove `Policy.Id = Guid.Empty` initializer | Declined | Seeder assigns explicit Guids for RISK-09 determinism. `Guid.Empty` is a harmless default that does not conflict with EF Core's `ValueGeneratedOnAdd()` since the seeder bypasses that path entirely. The reviewer's concern (accidental insert of `00000000-...`) is valid in principle but does not apply to this codebase: no code path constructs a `Policy` and persists it without explicitly setting `Id`. |
| Gate-2 | — | All findings applied; MIN-04 (Phase 5 TODO stubs) retained by design. | — | — |

---

## Phase 4 — Infrastructure Layer

**Date:** 2026-06-08
**ADRs governing this phase:** ADR-003 (no repository pattern), ADR-005 (enums as strings), ADR-006 (ExecuteUpdateAsync), ADR-009 (sort allow-list), ADR-010 (TimeProvider)

### What was built

- **PolicyDbContext** with fluent API configuration: unique index on `PolicyNumber`, `decimal(18,2)` precision on `PremiumAmount`, both enums stored as `nvarchar` strings (ADR-005), and automatic `CreatedAt`/`UpdatedAt` stamping via a shared `StampTimestamps()` private helper called from both `SaveChanges` and `SaveChangesAsync`. `TimeProvider` is constructor-injected (ADR-010); the interceptor guards `CreatedAt` with `== default(DateTime)` so explicitly assigned values (e.g., from the seeder) are preserved.
- **PolicyService** implementing `IPolicyService`: `IQueryable` composition with fully deferred execution, typed `Expression<Func<Policy, object>>` sort allow-list with `StringComparer.OrdinalIgnoreCase` (ADR-009), `ExecuteUpdateAsync` for bulk flag — single SQL `UPDATE` regardless of batch size (ADR-006), `TimeProvider` for expiry-soon calculation (ADR-010), `ExpiringSoonThresholdDays` read from `IConfiguration` with a default of 30 (RISK-02).
- **PolicyDataSeeder**: 250 deterministic records, `new Random(42)`, fixed timestamp `new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)`, 44 APAC policyholder names, 20 underwriters, all 4 statuses and LOBs (round-robin), all 8 correct regions (Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines), 6 currencies via `Currency` static class.
- **IDesignTimeDbContextFactory** for EF tooling: reads the `ConnectionStrings__DefaultConnection` environment variable and throws a clear `InvalidOperationException` if unset.
- **AddInfrastructure()** DI extension: registers `DbContext`, `PolicyService`, and `TimeProvider.System`.
- Startup migration + seed block in `Program.cs` (scope drift from Phase 5 — see below).
- **EF Core InitialCreate migration**: `LineOfBusiness` and `Status` as `nvarchar(50)` (ADR-005), `PremiumAmount` as `decimal(18,2)`, unique index on `PolicyNumber`, all 14 schema fields present.

### Gate-2 review: 11 findings, build was green throughout

The build was green before the review. Static review was the only mechanism available to catch the defects found. A passing build gives no signal about client-side evaluation fallback, timestamp overwrite conflicts, or security constraint violations in design-time classes. All 11 findings (1 CRITICAL, 4 MAJOR, 6 MINOR) were identified by reading and reasoning about the code against the requirements, ADRs, and CLAUDE.md constraints.

### The four smartest catches

**MAJ-04 — The GroupBy `.ToString()` client-side evaluation trap**

`GetSummaryAsync` originally called `g.Key.ToString()` inside a server-side `GroupBy` projection. EF Core cannot translate `.ToString()` on an enum inside a `Select` over a `GroupBy` key to SQL — it silently falls back to client-side evaluation, loading all policy rows into memory and aggregating in C#. The fix: GroupBy on the enum value directly (EF Core can translate that to a `GROUP BY` on the `nvarchar` column), then move `.ToString()` into the `ToDictionaryAsync` key selector, which runs after materialisation. This class of bug does not throw and produces no error — it silently degrades performance and defeats the server-side aggregation requirement. It is precisely why GroupBy projections deserve explicit scrutiny.

**CRIT-01 / MAJ-01 — The interceptor-vs-seeder timestamp conflict**

`PolicyDbContext.SaveChangesAsync` unconditionally stamped `CreatedAt = DateTime.UtcNow` on every `Added` entity. The seeder explicitly set `CreatedAt` to a fixed deterministic value before calling `SaveChangesAsync`. Two individually correct-looking files — the seeder setting the value, the interceptor overwriting it — combined into a bug that was invisible at compile time and at test time (no test asserted a specific seeded `CreatedAt`). The fix: guard with `entry.Entity.CreatedAt == default(DateTime)` so the interceptor only stamps when no value has been assigned. As a linked fix, the seeder's timestamp was changed from `DateTime.UtcNow` to the fixed `new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)` so every fresh database produces identical records.

**MAJ-03 — The hardcoded connection string in PolicyDbContextFactory**

`PolicyDbContextFactory` is design-time-only — it never runs in production — which makes a hardcoded connection string feel harmless. CLAUDE.md's prohibition on hardcoded connection strings has no carve-out for design-time files. A future developer could grep for connection strings, find this one, and copy it somewhere it does not belong. The fix: read from the `ConnectionStrings__DefaultConnection` environment variable and throw a clear `InvalidOperationException` if unset. The design-time tooling intent is fully preserved; the violation is not.

**MIN-03 — The reviewer catching its own seeder DoD failure**

The seeder's defined DoD required all 8 regions: Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines. The implementation included `"South Korea"` instead of `"Malaysia"`. The reviewer caught this by checking the actual region array against the requirements spec — not by checking that the code compiled or that the array had the right element count. Unit tests did not assert the exact region list; a requirements-aware review did.

### Scope drift: startup migration in Program.cs

The startup migration and seed block (`MigrateAsync` + `SeedAsync`) was added to `Program.cs` during Phase 4, whereas PLAN.md placed `Program.cs` wiring in Phase 5. The reviewer flagged this as scope drift (MIN-04). Human gate decision: **accepted with conditions**. The block is startup infrastructure, not API surface, and `Program.cs` is the correct location. The condition: the block must be wrapped in `try/catch` with structured logging before the phase is considered production-ready (MAJ-02). The corrector addressed this. The surrounding Phase 5 `TODO` stubs in `Program.cs` (Serilog, health checks, global error handling) are retained intentionally — they are Phase 5 work, not defects.

### Pros

- `PolicyService`'s `IQueryable` chain is genuinely deferred — `CountAsync` executes against the filtered-but-unpaged query, then `Skip`/`Take`/`ToListAsync` execute as a single terminal operation. No premature materialisation, no N+1 risk.
- The sort allow-list provides SQL injection protection at the expression level: only typed lambda expressions defined in source code reach EF Core's `OrderBy` — never a user-supplied string.
- `ExecuteUpdateAsync` for bulk flag issues a single SQL `UPDATE ... WHERE Id IN (...)` regardless of how many IDs are in the request. The change tracker is not involved.
- `TimeProvider` injection makes the expiry-soon calculation fully controllable in tests. `FakeTimeProvider` with a fixed date makes any expiring-soon assertion deterministic.
- Seeder determinism (`Random(42)` + fixed timestamp) means every fresh `docker-compose up` produces identical records.

### Cons / known limitations

- `GetSummaryAsync` makes three separate database round-trips (status counts, premium by LOB, expiring-soon count). Acceptable at assessment scale; a combined query or caching layer would be warranted under higher load.
- EF Core's InMemory provider (used in unit tests) does not enforce the unique index on `PolicyNumber`. A duplicate `PolicyNumber` passes unit tests but fails on real SQL Server. Tests that assert uniqueness enforcement require an integration test against a real database or SQLite with migrations.
- `IDesignTimeDbContextFactory` requires the `ConnectionStrings__DefaultConnection` environment variable to be set before running `dotnet ef` commands. Minor developer friction; the `InvalidOperationException` message documents the requirement.

### Walkthrough takeaway

This phase justifies the entire pipeline. Build green is not a quality bar. Static review caught defects that would have been silent runtime bugs — a client-side aggregation loading the full table, a timestamp conflict between two correct-looking files, a security constraint quietly violated in a dev-only class. The corrector addressed all 11 findings before the commit landed.
