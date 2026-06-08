# Development Plan — Policy Management BFF Service

**Prepared by:** Planner Agent
**Date:** 2026-06-08
**Based on:** `.specs/requirements.md` and `CLAUDE.md`

---

## Overview

This plan covers the full delivery of the Policy Management BFF Service for Chubb APAC. The service is a .NET 8 Web API following Clean Architecture, backed by SQL Server 2022, with Docker Compose for local operation and a contract-first OpenAPI 3.x specification.

Work is divided into six phases targeting a 2–5 hour sprint. Each phase has numbered tasks, time estimates, explicit dependencies, and a definition of done (DoD). Risks are called out in a dedicated section. The project-level DoD and a "What I Would Do Next" section close the document.

**Key simplifications over a full-scope plan:**
- Service layer replaces CQRS/MediatR: `IPolicyService` in Application, `PolicyService` in Infrastructure, controllers call the service directly.
- No separate repository interface or repository class: `PolicyService` uses `DbContext` directly with `IQueryable` composition.
- One test project (`PolicyManagement.UnitTests`) using EF Core InMemory provider — no Testcontainers, no separate integration test project.
- `Policy` is a clean POCO — no domain exceptions, factory methods, or guard-clause validation on the entity.

---

## Phase 1 — Foundation (Repository & Tooling)

**Goal:** Establish the repository, project skeleton, and local development environment before any domain or feature work begins.

### Task 1.1 — Git Repository Initialisation
- **Estimate:** 0.1 h
- **Description:** Initialise a Git repository with a meaningful initial commit. Add `.gitignore` for .NET, Docker, and IDE artefacts. Add a root `README.md` stub.
- **DoD:** `git log` shows at least one commit; `.gitignore` excludes `bin/`, `obj/`, `*.user`, and Docker volumes; `git status` is clean after a fresh clone and `dotnet build` would not commit build artefacts.

### Task 1.2 — Solution and Project Scaffold
- **Estimate:** 0.25 h
- **Description:** Create the solution file and five C# projects representing the Clean Architecture layers:
  - `PolicyManagement.Domain` (class library)
  - `PolicyManagement.Application` (class library)
  - `PolicyManagement.Infrastructure` (class library)
  - `PolicyManagement.API` (ASP.NET Core Web API)
  - `PolicyManagement.UnitTests` (xUnit class library)
- **DoD:** `dotnet build` succeeds from the solution root with zero errors and zero warnings; project references enforce the inward-only dependency rule (API → Infrastructure → Application → Domain); UnitTests references Application and Infrastructure; no project references point outward from inner layers.

### Task 1.3 — Docker Compose Local Environment
- **Estimate:** 0.2 h
- **Description:** Author `docker-compose.yml` with services for SQL Server 2022 and the API application. Include a `docker-compose.override.yml` for local developer overrides. Connection strings and secrets must be passed via environment variables or Docker secrets, never hardcoded.
- **DoD:** `docker-compose up` starts both containers without error; SQL Server healthcheck passes; API container starts and responds on its configured port; no secrets appear in any committed file.

### Task 1.4 — Configuration Baseline
- **Estimate:** 0.1 h
- **Description:** Set up `appsettings.json`, `appsettings.Development.json`, and the pattern for reading secrets from environment variables. Define configuration sections for database connection, logging, and feature flags (placeholder).
- **DoD:** Running the API locally picks up connection string from environment variable `ConnectionStrings__DefaultConnection`; secrets are not present in any committed configuration file; `dotnet user-secrets` is documented as the local development approach.

**Phase 1 total estimate:** ~0.6 h
**Phase 1 prerequisite:** None — this phase unblocks all subsequent work.

---

## Phase 2 — Domain Layer

**Goal:** Define the core business concepts and enumerations with no dependency on infrastructure, ORMs, or HTTP. `Policy` is a plain POCO — no guard clauses, no factory methods, no domain exceptions.

### Task 2.1 — Core Enumerations
- **Estimate:** 0.1 h
- **Description:** Define `PolicyStatus` enum (`Active`, `Expired`, `Pending`, `Cancelled`) and `LineOfBusiness` enum (`Property`, `Casualty`, `AandH`, `Marine`) in the Domain project.
- **DoD:** Both enums exist in `PolicyManagement.Domain`; no references to EF Core, ASP.NET, or any external library appear in the Domain project file.

### Task 2.2 — Policy POCO
- **Estimate:** 0.15 h
- **Description:** Define the `Policy` class with all fields from the data schema: `Id` (Guid), `PolicyNumber` (string, format POL-XXXXXX), `PolicyholderName`, `LineOfBusiness`, `Status`, `PremiumAmount` (decimal), `Currency`, `EffectiveDate`, `ExpiryDate`, `Region`, `Underwriter`, `FlaggedForReview` (bool, default false), `CreatedAt`, `UpdatedAt`. All properties have public getters and setters. No constructor validation, no factory methods, no domain exceptions.
- **DoD:** `Policy` class compiles in the Domain project with auto-properties only; no EF Core attributes or infrastructure references; the Domain project has no NuGet dependencies beyond the .NET SDK.

**Phase 2 total estimate:** ~0.25 h
**Phase 2 prerequisite:** Task 1.2 (projects must exist).

---

## Phase 3 — Application Layer (Service Interface & DTOs)

**Goal:** Define the `IPolicyService` interface and all response DTOs. No infrastructure or HTTP concerns. This layer contains the contract that controllers depend on.

### Task 3.1 — Application DTOs
- **Estimate:** 0.25 h
- **Description:** Define all response DTOs in the Application layer: `PolicyListItemDto`, `PolicyDetailDto`, `PolicySummaryDto`, `PaginatedResult<T>`, `BulkFlagResultDto`. DTOs must be immutable (records or init-only properties). Also define `PolicyListQuery` as a plain parameter object carrying all filter, sort, and pagination fields.
- **DoD:** All DTOs are C# records or have init-only setters; no EF Core or infrastructure types are referenced; the Application project has no NuGet dependencies beyond the .NET SDK.

### Task 3.2 — IPolicyService Interface
- **Estimate:** 0.15 h
- **Description:** Define `IPolicyService` in the Application layer with the following methods:
  - `Task<PaginatedResult<PolicyListItemDto>> ListAsync(PolicyListQuery query, CancellationToken ct)`
  - `Task<PolicyDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)`
  - `Task<BulkFlagResultDto> BulkFlagAsync(IReadOnlyList<Guid> ids, CancellationToken ct)`
  - `Task<PolicySummaryDto> GetSummaryAsync(CancellationToken ct)`
- **DoD:** Interface declared in `PolicyManagement.Application`; references only Domain types and Application DTOs; no concrete implementations exist yet; method signatures cover all list-endpoint filter fields defined in requirements.

**Phase 3 total estimate:** ~0.4 h
**Phase 3 prerequisite:** Task 1.2; Phase 2 complete.

---

## Phase 4 — Infrastructure Layer

**Goal:** Implement persistence using Entity Framework Core against SQL Server 2022, including migrations, a realistic data seed, and the `PolicyService` implementation that uses `DbContext` directly.

### Task 4.1 — EF Core DbContext and Configuration
- **Estimate:** 0.35 h
- **Description:** Create `PolicyDbContext` in the Infrastructure project. Configure the `Policy` entity using fluent API (not data annotations): column types, precision for `PremiumAmount` (decimal(18,2)), `PolicyNumber` uniqueness index, enum conversions stored as strings (RISK-06), `CreatedAt`/`UpdatedAt` set automatically. Register the DbContext in DI via Infrastructure's `IServiceCollection` extension method.
- **DoD:** `dotnet ef dbcontext info` resolves the context without error; all column types match the schema definition; no Data Annotations appear on the Domain entity; enum columns store string values (verified by inspecting the migration snapshot).

### Task 4.2 — Initial Migration
- **Estimate:** 0.15 h
- **Description:** Generate the initial EF Core migration creating the `Policies` table. Migration must be idempotent and re-runnable. Document the migration naming convention in the journal.
- **DoD:** `dotnet ef database update` applied against a blank SQL Server database creates the `Policies` table with all correct columns; rolling back the migration drops the table cleanly; migration file is committed to source control.

### Task 4.3 — PolicyService Implementation
- **Estimate:** 0.75 h
- **Description:** Implement `PolicyService : IPolicyService` in the Infrastructure project. The class takes `PolicyDbContext` via constructor injection and uses `IQueryable` composition throughout. No repository interface, no repository class — `DbContext` is used directly. Key requirements per method:
  - `ListAsync`: Build an `IQueryable<Policy>` pipeline applying status/lineOfBusiness/region/date-range filters, free-text search (`LIKE` across `PolicyNumber`, `PolicyholderName`, `Underwriter`), dynamic sorting via an allow-list mapped to `Expression<Func<Policy, object>>` selectors (RISK-04), and pagination via `Skip`/`Take`. Defer execution to a single database round-trip. Project directly to `PolicyListItemDto`.
  - `GetByIdAsync`: Single record fetch by GUID; return `null` if not found (controller maps null to 404).
  - `BulkFlagAsync`: Update `FlaggedForReview = true` and `UpdatedAt` for a batch of IDs using `ExecuteUpdateAsync` (no loading entities into memory). Return `{ Flagged: N, NotFound: M }` — process all valid IDs, silently skip unknown ones (RISK-01 resolution). Operation is idempotent.
  - `GetSummaryAsync`: Use `GroupBy` projections to compute counts and sums in the database; compute expiring-soon count (policies with `Status = Active` expiring within a configurable threshold — default 30 days from `appsettings.json`, RISK-02 resolution) in a separate query. Use `TimeProvider` (injected) for the current date reference.
- **DoD:** All methods use native async EF Core calls; no synchronous I/O; `ListAsync` produces a single SQL query (verified by EF Core query logging or unit test assertion); `BulkFlagAsync` does not load entities into memory; `PolicyService` is registered in DI as `IPolicyService`; all unit tests from Phase 5 pass.

### Task 4.4 — Data Seed
- **Estimate:** 0.3 h
- **Description:** Write a `PolicyDataSeeder` that inserts 200+ realistic policy records on first run using a fixed random seed (`new Random(42)`) for determinism (RISK-09). Seed data must cover: all four `LineOfBusiness` values, all four `Status` values, all eight regions, a realistic spread of effective/expiry dates (past, current, future), premiums in the range 1,000–5,000,000, all six currencies, APAC-style policyholder names, and a mix of `FlaggedForReview` values. The seeder must be idempotent (check for existing records before inserting).
- **DoD:** Running `docker-compose up` on a fresh database populates 200+ records; re-running does not duplicate data; `GET /api/v1/policies/summary` returns non-zero counts for every status and every line of business; all eight regions are represented; fixed random seed ensures identical data on every fresh run.

**Phase 4 total estimate:** ~1.55 h
**Phase 4 prerequisite:** Phase 2 complete; Phase 3 complete (IPolicyService must exist); Task 1.3 (Docker Compose with SQL Server) complete.

---

## Phase 5 — API Layer

**Goal:** Expose the four endpoints via ASP.NET Core Web API controllers, wire up middleware, and publish the OpenAPI specification.

### Task 5.1 — OpenAPI Contract (openapi.yaml)
- **Estimate:** 0.35 h
- **Description:** Author the full `openapi.yaml` (OpenAPI 3.x) specification before writing controller code. The spec must define: all four endpoints with correct HTTP methods and paths (note RISK-07 on route ordering), all query parameters for the list endpoint with types and defaults, the `Policy` schema with all fields and enum values as strings (RISK-06), the `PolicySummary` schema, the `BulkFlagRequest` schema (array of UUIDs), the `PaginatedResult` schema, and standard error responses (400, 404, 500). This spec is the source of truth; controllers must conform to it.
- **DoD:** `openapi.yaml` is valid per the OpenAPI 3.x specification (validated with a linter such as `spectral` or the Swagger Editor); all four endpoints are described; all schema fields match the data schema in requirements; the file is committed at `docs/openapi.yaml`.

### Task 5.2 — Policies Controller
- **Estimate:** 0.4 h
- **Description:** Implement `PoliciesController` with four action methods. Each action method:
  - Accepts and validates request parameters (data annotations or minimal inline validation)
  - Calls the appropriate `IPolicyService` method directly
  - Returns the correct HTTP status code (200, 204, 400, 404)
  - Maps query parameters to the `PolicyListQuery` object or relevant method arguments
  - The `/flag` route must be declared before the `{id:guid}` route to resolve RISK-07
- **DoD:** All four endpoints return the correct HTTP status codes as documented in the OpenAPI spec; no business logic lives in the controller; controller action methods are under 40 lines each; `null` return from `GetByIdAsync` produces a 404 Problem Details response.

### Task 5.3 — Request Validation
- **Estimate:** 0.2 h
- **Description:** Validate all inbound request parameters. `page` and `size` must be positive integers with upper bounds (max size 100). `sort` field must be one of the allowed column names (reuse the same allow-list as RISK-04 in `PolicyService`). Enum filter values must map to valid enum members. `effectiveDateFrom` must not be after `effectiveDateTo`. Return 400 with a structured Problem Details body on validation failure.
- **DoD:** Unit tests verify that invalid `page`/`size` values return 400; invalid enum strings return 400; date range inversion returns 400; valid requests pass through without error.

### Task 5.4 — Global Error Handling and Problem Details
- **Estimate:** 0.2 h
- **Description:** Implement a global exception handler (using `app.UseExceptionHandler` or `IProblemDetailsService`) that maps unhandled exceptions → 500 Problem Details. All error responses use RFC 7807 Problem Details format (`application/problem+json`). Null returns from the service are handled at the controller level (not via exceptions) and produce 404 Problem Details responses.
- **DoD:** A request for a non-existent policy ID receives a 404 response with `application/problem+json` content type and a `detail` field; an artificial unhandled exception produces a 500 with no stack trace exposed in non-Development environments.

### Task 5.5 — Health Checks
- **Estimate:** 0.15 h
- **Description:** Add ASP.NET Core health checks: a liveness probe at `GET /health/live` and a readiness probe at `GET /health/ready` (which includes a database connectivity check via EF Core).
- **DoD:** `GET /health/live` returns 200 always when the process is running; `GET /health/ready` returns 200 when the database is reachable and 503 when it is not (testable by stopping the database container).

### Task 5.6 — Structured Logging
- **Estimate:** 0.15 h
- **Description:** Configure structured logging via Serilog (or the built-in `ILogger` with JSON output). Log: incoming request method and path, handler entry and exit with duration, and exceptions with a correlation ID. Use log levels correctly (Debug for internals, Information for normal operations, Warning for handled errors, Error for unhandled). Enrich logs with environment name.
- **DoD:** Running `docker-compose up` and making several API calls produces JSON-structured log output in the container; logs include request path, duration, and status code; no sensitive data is logged at Information level.

### Task 5.7 — Swagger UI Integration
- **Estimate:** 0.1 h
- **Description:** Wire Swashbuckle (or Scalar) to serve interactive API documentation at `/swagger` in Development. Configure it to serve from `docs/openapi.yaml` or generate from the controller metadata.
- **DoD:** Navigating to `/swagger` in a running local instance renders all four endpoints; Swagger UI is disabled in Production.

**Phase 5 total estimate:** ~1.55 h
**Phase 5 prerequisite:** Phase 3 complete (IPolicyService); Phase 4 complete (PolicyService implementation); Task 1.3 (Docker Compose).

---

## Phase 6 — Testing

**Goal:** Achieve production-quality unit test coverage using xUnit and the EF Core InMemory provider. One test project: `PolicyManagement.UnitTests`.

### Task 6.1 — PolicyService Unit Tests
- **Estimate:** 0.65 h
- **Description:** Unit tests for all four `PolicyService` methods using EF Core InMemory provider (`UseInMemoryDatabase`). Seed the in-memory context with known records in each test's Arrange section. Tests cover:
  - `ListAsync`: pagination, each filter type, sort direction, free-text search returning correct results, empty result set.
  - `GetByIdAsync`: returns correct DTO for a known record; returns null for an unknown ID.
  - `BulkFlagAsync`: flags targeted policies; idempotency (already-flagged policies do not increment count); partial-success when some IDs are unknown; correct `Flagged` and `NotFound` counts.
  - `GetSummaryAsync`: correct counts by status, correct premium totals by line of business, correct expiring-soon count using a controlled `TimeProvider`.
- **DoD:** All tests pass in isolation; no test shares mutable `DbContext` state (each test creates a fresh InMemory database with a unique name); `TimeProvider` is injected and controlled in summary tests; minimum two tests per method (happy path + one edge/error case); tests follow Arrange-Act-Assert.

### Task 6.2 — Validation and Middleware Tests
- **Estimate:** 0.2 h
- **Description:** Tests verifying that invalid input returns 400 Problem Details and that the global error handler returns Problem Details for 500. Use `WebApplicationFactory<Program>` with the InMemory provider substituted for SQL Server.
- **DoD:** At least four tests covering: invalid pagination (`page=0`, `size=0`), invalid enum string, date range inversion, and an artificial 500; all return correct status codes and `application/problem+json` content type.

**Phase 6 total estimate:** ~0.85 h
**Phase 6 prerequisite:** Phase 4 (PolicyService) and Phase 5 (API) complete.

---

## Dependency Order Summary

| Task | Blocked By | Blocks |
|---|---|---|
| 1.1 Git Init | — | Everything |
| 1.2 Solution Scaffold | 1.1 | All code tasks |
| 1.3 Docker Compose | 1.1 | 4.1, 4.2, 4.4, 5.x, 7.x |
| 1.4 Configuration | 1.2 | 4.1, 5.x |
| 2.1 Enumerations | 1.2 | 2.2, 3.x, 4.x |
| 2.2 Policy POCO | 2.1 | 3.x, 4.x |
| 3.1 Application DTOs | 2.2 | 3.2, 4.3, 5.2 |
| 3.2 IPolicyService | 3.1 | 4.3, 5.2 |
| 4.1 DbContext | 2.2, 1.3, 1.4 | 4.2, 4.3 |
| 4.2 Initial Migration | 4.1 | 4.4 |
| 4.3 PolicyService Impl | 4.1, 3.2 | 5.2, 6.1 |
| 4.4 Data Seed | 4.2 | 7.x |
| 5.1 OpenAPI Contract | 2.x, 3.1 | 5.2 (spec drives controllers) |
| 5.2 Controller | 3.2, 4.3, 5.1 | 6.2 |
| 5.3 Validation | 5.2 | 6.2 |
| 5.4 Error Handling | 5.2 | 6.2 |
| 5.5 Health Checks | 5.2, 4.1 | — |
| 5.6 Logging | 5.2 | — |
| 5.7 Swagger UI | 5.1, 5.2 | — |
| 6.1 PolicyService Tests | 4.3 | — |
| 6.2 Validation Tests | 5.3, 5.4 | — |

**Critical path:** 1.1 → 1.2 → 2.1 → 2.2 → 3.1 → 3.2 → 4.3 → 5.2 → 6.2

---

## Risk Flags

### RISK-01 — Ambiguous bulk-flag partial-failure behaviour
- **Location:** Requirements §API Endpoints (PATCH /api/v1/policies/flag)
- **Risk:** The requirements do not specify what should happen when some IDs in the bulk-flag request do not exist.
- **Recommended Resolution:** Adopt option (b) — process all valid IDs, silently skip unknown ones, and return `{ "flagged": N, "notFound": M }`. This aligns with the idempotency requirement and avoids client-side partial-retry complexity. Log the decision in the AI journal.

### RISK-02 — "Expiring soon" threshold not defined
- **Location:** Requirements §API Endpoints (GET /api/v1/policies/summary)
- **Risk:** The requirements mention "expiring-soon count" without specifying the threshold.
- **Recommended Resolution:** Implement with a configurable threshold (default 30 days) read from `appsettings.json` under `PolicySummary:ExpiringSoonThresholdDays`. Document the default and make it overridable without redeployment.

### RISK-03 — Free-text search performance at scale
- **Location:** Requirements §List Endpoint Parameters (search across three fields)
- **Risk:** A `LIKE '%term%'` query across `PolicyNumber`, `PolicyholderName`, and `Underwriter` will not use standard B-tree indexes and will degrade with data volume. At 200 seed records this is invisible; in production it will be problematic.
- **Recommended Resolution:** For this assessment, implement the `LIKE` approach and add a note in the ADR and journal flagging that Full-Text Search or a dedicated search index would be required for production. Do not block implementation on this risk.

### RISK-04 — Dynamic sorting SQL injection surface
- **Location:** Task 4.3 / List Endpoint
- **Risk:** Dynamic column sorting from user input (e.g., `sort=premiumAmount,desc`) can create SQL injection risk if the column name is interpolated directly into a query.
- **Recommended Resolution:** Implement sorting using an allow-list of valid sort column names mapped to `Expression<Func<Policy, object>>` selectors. Reject any sort field not in the allow-list with a 400 response. Never pass raw user input to `OrderBy` via string interpolation. Reuse this allow-list in Task 5.3 for request validation.

### RISK-05 — EF Core InMemory provider limitations in tests
- **Location:** Task 6.1
- **Risk:** The EF Core InMemory provider does not enforce referential integrity, unique constraints, or check constraints. Tests that rely on database-level constraint violations will give false positives.
- **Recommended Resolution:** For this sprint, InMemory is sufficient because all constraint enforcement is done in application code (allow-list validation, duplicate-check in seed). Document this limitation. Production readiness would require SQL Server-backed tests (Testcontainers) — see "What I Would Do Next".

### RISK-06 — EF Core enum storage format
- **Location:** Task 4.1
- **Risk:** By default, EF Core stores enums as integers. The requirements use string enum values in the API (e.g., `"Active"`, `"Property"`). If integers are stored in the DB, queries filtering by enum string from the API will require careful mapping; readable DB content is also lost.
- **Recommended Resolution:** Configure EF Core to store all enums as strings using `.HasConversion<string>()` in the fluent configuration. This aligns with the contract-first API, makes the database human-readable, and simplifies debugging. Note: renaming an enum value becomes a breaking migration change — document this constraint.

### RISK-07 — PATCH /api/v1/policies/flag URL conflicts with /api/v1/policies/{id}
- **Location:** Requirements §API Endpoints
- **Risk:** The route `PATCH /api/v1/policies/flag` could be ambiguous if the routing engine attempts to match `"flag"` as a policy ID (UUID).
- **Recommended Resolution:** Define the `flag` route with an explicit route template that precedes the `{id}` route in the controller, and use a route constraint on `{id}` (`[Route("{id:guid}")]`) to ensure the routing engine never attempts to parse "flag" as a GUID. Verify with an explicit routing test or manual check during Task 5.2.

### RISK-08 — Currency as free-text string vs. enumeration
- **Location:** Requirements §Policy Data Schema (currency field)
- **Risk:** The requirements list six currencies but define `currency` as a `String`, not an enum. This allows invalid currencies to be inserted (relevant if write endpoints are added later) and complicates filtering.
- **Recommended Resolution:** For this sprint, store currency as a plain string. No check constraint is added because no write endpoints exist that could insert an invalid value — the only inserter is the seeder, which uses values from a known list. Define a `Currency` constant class in Domain holding the six allowed values for use by the seeder and potential future write endpoints. Document this as YAGNI in the journal: the constraint would be added the moment a write endpoint is introduced.

### RISK-09 — Seed data determinism and reproducibility
- **Location:** Task 4.4
- **Risk:** If seed data uses `Random` without a fixed seed, each fresh `docker-compose up` will produce different data, making tests that depend on specific values non-deterministic.
- **Recommended Resolution:** Use a fixed random seed (`new Random(42)`) for all generated seed data. This ensures `docker-compose up` always produces the same 200+ records, making any environment-level verification reproducible.

---

## Total Estimated Effort

| Phase | Estimate |
|---|---|
| Phase 1 — Foundation | 0.6 h |
| Phase 2 — Domain | 0.25 h |
| Phase 3 — Application | 0.4 h |
| Phase 4 — Infrastructure | 1.55 h |
| Phase 5 — API | 1.55 h |
| Phase 6 — Testing | 0.85 h |
| **Core total** | **~5.2 h** |

> Note: The core total sits at the upper edge of the 2–5 h sprint window. If time is tight, defer Task 5.6 (Structured Logging) and Task 5.7 (Swagger UI) without losing functional completeness; doing so brings the total to ~4.9 h. Phase 1.1 (Git init) is already complete — subtract 0.1 h from the Foundation phase for a fresh-start estimate of ~5.1 h including all cross-cutting concerns.

---

## Project-Level Definition of Done

The project is complete when ALL of the following conditions are true:

1. **Builds cleanly:** `dotnet build` from the solution root produces zero errors and zero warnings.

2. **All tests pass:** `dotnet test` runs all unit tests; no test is skipped, ignored, or failing. Code coverage for Application and Infrastructure (PolicyService) is at least 80%.

3. **Docker Compose works end-to-end:** `docker-compose down -v && docker-compose up` from a fresh state starts the API and SQL Server, runs migrations, seeds 200+ records, and the API is reachable within 60 seconds.

4. **All four endpoints respond correctly:**
   - `GET /api/v1/policies` returns a paginated list with correct structure.
   - `GET /api/v1/policies/{id}` returns a policy for a valid UUID and 404 Problem Details for an unknown UUID.
   - `PATCH /api/v1/policies/flag` sets `flaggedForReview = true` on specified policies and returns flagged/notFound counts.
   - `GET /api/v1/policies/summary` returns non-zero counts for all statuses and all lines of business.

5. **No hardcoded secrets:** A `git grep` for connection strings, passwords, or API keys in source files finds nothing.

6. **OpenAPI spec is valid and present:** `docs/openapi.yaml` exists, passes OpenAPI 3.x linting, and matches the deployed API behaviour.

7. **Health checks pass:** `GET /health/live` and `GET /health/ready` both return 200 with the database running.

8. **Structured logs are produced:** Container log output is JSON-structured with request path, status code, and duration.

9. **AI Working Journal is complete:** `docs/AI_WORKING_JOURNAL.md` contains at least one entry per phase documenting accepted, challenged, or overridden decisions.

10. **README enables zero-context onboarding:** A developer unfamiliar with the project can follow `README.md` and have a running local instance within 10 minutes.

11. **Clean Architecture constraints are satisfied:** No Domain or Application type references EF Core, ASP.NET Core, or any infrastructure library (verified by inspecting project file references).

12. **All risks are resolved:** Each risk flag above (RISK-01 through RISK-09) has a documented resolution committed to the AI working journal before the relevant task is implemented.

---

## What I Would Do Next

These items are deliberately out of scope for this sprint. They represent the natural next investments to make the service production-ready.

- **Testcontainers integration tests:** Replace the EF Core InMemory provider in tests with `Testcontainers.MsSql` to run against a real SQL Server engine. This catches constraint violations, migration regressions, and SQL dialect differences that InMemory silently ignores. Add a separate `PolicyManagement.IntegrationTests` project for these.

- **Distributed caching for summary:** Add Redis (via Docker Compose) and cache `GET /api/v1/policies/summary` with a configurable TTL (default 60 s). Invalidate the cache entry after every successful `BulkFlagAsync` call. Cache key must be deterministic and documented.

- **Kafka event streaming:** Add a Kafka service to Docker Compose. Implement a producer that publishes a `PolicyFlaggedEvent` (JSON, schema documented) after every successful bulk-flag operation. Implement a consumer that receives status-change events and updates policy status idempotently (duplicate messages must not cause duplicate updates). Add a Kafka health check.

- **Authentication and authorisation:** Add JWT bearer token validation (Azure AD or a compatible OIDC provider) to protect all four endpoints. Define roles (e.g., `policy.read`, `policy.flag`) and enforce them at the controller level. This was explicitly out of scope per requirements but is non-negotiable for any production deployment.
