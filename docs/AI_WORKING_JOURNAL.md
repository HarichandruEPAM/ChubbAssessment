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
