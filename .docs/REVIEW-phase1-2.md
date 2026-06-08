# Review Report — Phase 1 & 2
**Reviewer:** Reviewer Agent
**Date:** 2026-06-08
**Scope:** Phase 1 (Foundation) and Phase 2 (Domain Layer)

---

## CRITICAL findings

None.

---

## MAJOR findings

### MAJOR-01 — Program.cs contains no infrastructure DI wiring

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.API\Program.cs` (entire file)
- **Standard:** PLAN.md Task 1.2; ARCHITECTURE.md §3.4; ADR-001
- **Issue:** `Program.cs` is the verbatim ASP.NET Core scaffold. It calls `AddControllers()` and `AddSwaggerGen()` but does not call any `AddInfrastructure()` or `AddApplication()` extension method, does not wire health checks, does not configure a global exception handler, and does not configure Serilog or structured logging. While Phases 4–5 are responsible for the full implementation of those concerns, the PLAN.md Task 1.2 Definition of Done requires the scaffold to be clean and ready to receive those registrations — which it is — but it also states the app must "pick up connection string from environment variable" on run. Without any `DbContext` registration or DI wiring, the application would start in a state that silently ignores the externalized connection string completely. This is acceptable only if the downstream builder agent (Phase 4) fills in the wiring; however, the current state means `dotnet run` produces a working HTTP server with no infrastructure at all and no indication of missing configuration. This creates a false-green local start that masks wiring gaps. The scaffold should at minimum contain a `// TODO: AddInfrastructure(builder.Configuration)` placeholder or an explicit note so reviewers can confirm intent.
- **What must change:** Add a clearly marked placeholder comment for `AddInfrastructure` and `AddApplication` registration calls so no downstream agent accidentally ships without them. Alternatively, implement the shell calls referencing not-yet-existing extension methods and use `#if false` guards — but at minimum the missing wiring must be visible.

### MAJOR-02 — UnitTests project missing API project reference and `Microsoft.AspNetCore.Mvc.Testing`

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.UnitTests\PolicyManagement.UnitTests.csproj` (lines 26–29)
- **Standard:** ARCHITECTURE.md §1 (UnitTests layer table), §3.5; PLAN.md Task 6.2
- **Issue:** ARCHITECTURE.md explicitly states UnitTests "references Application, Infrastructure, and API (for WebApplicationFactory)". The csproj currently references only Application and Infrastructure. `Microsoft.AspNetCore.Mvc.Testing` is listed in the ARCHITECTURE.md NuGet package list for UnitTests but is absent from the csproj. Without the API project reference and this package, Task 6.2 (WebApplicationFactory integration tests for 400/500 responses) cannot be implemented at all. Discovering this gap in Phase 6 would require a csproj edit that could cascade into dependency resolution issues at a time when the team is under testing pressure.
- **What must change:** Add `<ProjectReference Include="..\PolicyManagement.API\PolicyManagement.API.csproj" />` and `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.*" />` to `PolicyManagement.UnitTests.csproj` now, consistent with the documented architecture.

---

## MINOR findings

### MINOR-01 — API.csproj contains a redundant direct reference to Domain

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.API\PolicyManagement.API.csproj` (line 6)
- **Standard:** ARCHITECTURE.md §1 (API csproj template); ADR-001
- **Issue:** The ARCHITECTURE.md template for `PolicyManagement.API.csproj` shows only two project references: Infrastructure and Application. The actual file adds a third direct reference to Domain (`PolicyManagement.Domain.csproj`). This reference is not a Clean Architecture violation — the API is the composition root and is permitted to reference any inner layer — but it is redundant because Infrastructure already brings in Application, and Application already brings in Domain transitively. The redundant explicit reference deviates from the stated design template without any documented justification, which could mislead future developers into thinking the API is expected to use Domain types directly rather than through DTOs.
- **What must change:** Remove the direct `PolicyManagement.Domain` project reference from `PolicyManagement.API.csproj`, or add a comment to the csproj and to ARCHITECTURE.md documenting why the direct reference is intentional (e.g., if controllers need to reference `PolicyStatus` enum directly in action parameters).

### MINOR-02 — `docker-compose.override.yml` is listed in `.gitignore` yet is present as a committed file

- **File:** `C:\Projects\ChubbAssessment\.gitignore` (line 59); `C:\Projects\ChubbAssessment\docker-compose.override.yml`
- **Standard:** PLAN.md Task 1.1 (gitignore must exclude Docker volumes and developer artefacts); PLAN.md Task 1.3 (override for local developer overrides)
- **Issue:** `.gitignore` line 59 explicitly ignores `docker-compose.override.yml`, which is the correct pattern for developer-local override files that contain secrets. However, `docker-compose.override.yml` is present in the working tree and appears to have been committed (or is tracked). If the file was committed before the gitignore rule was added, git continues to track it regardless of the gitignore entry. This means the file — even though it currently contains no secrets — is visible in source control, and any future developer who adds local credentials to it will accidentally commit them. The intent (per the file's own comment) is correct, but the gitignore rule and committed state conflict.
- **What must change:** If the override file is intended to be a developer-local file (the gitignore rule says it should be), run `git rm --cached docker-compose.override.yml` to stop tracking it while keeping it on disk. If it is intentionally committed as a template (like `.env.example`), rename it to `docker-compose.override.example.yml` and update the gitignore accordingly.

### MINOR-03 — `DomainPlaceholder.cs` is present and should be removed

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.Domain\DomainPlaceholder.cs` (all 3 lines)
- **Standard:** PLAN.md Task 2.2 DoD; CLAUDE.md (meaningful names; no unexplained artefacts)
- **Issue:** `DomainPlaceholder.cs` was presumably created to satisfy the build requirement of having at least one compilable file in the Domain project before the domain types were written. Now that `Policy.cs`, `PolicyStatus.cs`, `LineOfBusiness.cs`, and `Currency.cs` all exist and compile, the placeholder file has no function. It declares an empty `PolicyManagement.Domain` namespace and adds noise to the project.
- **What must change:** Delete `DomainPlaceholder.cs`. The Domain project compiles correctly without it.

### MINOR-04 — `Policy.Id` defaults to `Guid.Empty` instead of being uninitialized or defaulting at the persistence layer

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.Domain\Entities\Policy.cs` (line 7)
- **Standard:** PLAN.md Task 2.2 ("All properties have public getters and setters. No constructor validation, no factory methods, no domain exceptions."); CLAUDE.md (meaningful names; SOLID principles)
- **Issue:** `public Guid Id { get; set; } = Guid.Empty;` explicitly initializes `Id` to `Guid.Empty`. This is a latent correctness risk: if a `Policy` object is ever constructed without setting `Id` and then persisted, EF Core will insert a row with `00000000-0000-0000-0000-000000000000` as the primary key rather than throwing a constraint violation (EF Core's value generation behaviour for `Guid` depends on configuration). The PLAN.md states `Policy` is a plain POCO with no guard clauses, which is correct — but the `= Guid.Empty` initializer actively sets a bad value rather than leaving the property at the CLR default (`Guid.Empty` is also the CLR default for `Guid`, so the initializer is redundant). The risk is low for this sprint because no write endpoints exist, but it is misleading compared to, for example, leaving the property without an initializer and relying on EF Core's `ValueGeneratedOnAdd()` configuration.
- **What must change:** Remove the `= Guid.Empty` initializer from `Id`. The property should read `public Guid Id { get; set; }`. EF Core's entity configuration (Task 4.1) will apply `ValueGeneratedOnAdd()` to ensure new IDs are generated at insert time.

---

## OBSERVATIONS

### OBS-01 — `Infrastructure.csproj` uses floating version wildcards (`8.0.*`)

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.Infrastructure\PolicyManagement.Infrastructure.csproj` (lines 9–13)
- **Standard:** CLAUDE.md (production-quality standards; prefer choices that match the stated production environment)
- **Observation:** `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Tools` are referenced with version `8.0.*`. Floating wildcards cause non-deterministic builds: two developers restoring at different times may get different patch versions, which is acceptable for assessment code but non-standard for production-quality repositories. Consider pinning to specific versions (e.g., `8.0.11`) and using `dotnet outdated` or Dependabot for managed updates.

### OBS-02 — `appsettings.Development.json` uses a non-standard `Description` property

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.API\appsettings.Development.json` (line 3)
- **Standard:** PLAN.md Task 1.4
- **Observation:** The file includes a `"Description"` key at the root level as a documentation comment. While harmless (ASP.NET Core ignores unknown keys), JSON configuration files are not the appropriate place for human-readable documentation. The information belongs in a README or CLAUDE.md note. No change is strictly required but the key could be misread as an intentional configuration value by future maintainers.

### OBS-03 — `docker-compose.yml` healthcheck uses `-No` flag which may differ between `mssql-tools` versions

- **File:** `C:\Projects\ChubbAssessment\docker-compose.yml` (line 22)
- **Standard:** PLAN.md Task 1.3 DoD
- **Observation:** The healthcheck uses `/opt/mssql-tools18/bin/sqlcmd` with flag `-No` (trust server certificate). The `mssql-tools18` path is specific to the tools18 package; if the SQL Server 2022 image ships a different tools version in a future update, the hardcoded path will break the healthcheck silently. This is a low-risk observation for an assessment but worth noting for a production deployment.

### OBS-04 — `Serilog.AspNetCore` is referenced in ARCHITECTURE.md but not in `PolicyManagement.API.csproj`

- **File:** `C:\Projects\ChubbAssessment\src\PolicyManagement.API\PolicyManagement.API.csproj`; ARCHITECTURE.md §1
- **Standard:** ARCHITECTURE.md §1 (API NuGet packages list)
- **Observation:** ARCHITECTURE.md states the API project NuGet packages include `Swashbuckle.AspNetCore` and `Serilog.AspNetCore`. The csproj contains only `Swashbuckle.AspNetCore`. `Serilog.AspNetCore` is a Phase 5 (Task 5.6) concern and its absence is expected at this stage. This is recorded as an observation so the Phase 5 implementer knows to add it.

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| MAJOR | 2 |
| MINOR | 4 |
| OBSERVATION | 4 |

## Verdict

APPROVE WITH MINOR

## Recommendation

The Phase 1 and Phase 2 deliverables are structurally sound. The Clean Architecture dependency rules are correctly enforced in the project files, the Domain layer is a pure POCO with no framework contamination, all security constraints are met (no hardcoded secrets, correct gitignore rules, placeholder-only `.env.example`), and the configuration baseline satisfies the PLAN.md Task 1.4 requirements.

Two issues must be addressed before downstream phases build on this foundation. First, `PolicyManagement.UnitTests.csproj` is missing the API project reference and `Microsoft.AspNetCore.Mvc.Testing` package that the architecture document requires; adding these now costs minutes but discovering the gap in Phase 6 costs a cascade of dependency changes under time pressure. Second, `Program.cs` must have clearly marked placeholder hooks for `AddInfrastructure` and `AddApplication` so no phase can be considered complete without them — the current silence makes the missing wiring invisible. The four minor findings (redundant Domain reference in API csproj, docker-compose.override.yml tracking conflict, DomainPlaceholder.cs presence, and the `Guid.Empty` initializer) are all low-risk but straightforward to fix and should be resolved before Phase 4 begins. Proceed to Phase 3 after resolving MAJOR-01 and MAJOR-02.
