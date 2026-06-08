# Architecture — Policy Management BFF Service

**Prepared by:** Architect Agent
**Date:** 2026-06-08
**Based on:** `CLAUDE.md`, `.specs/requirements.md`, `.docs/PLAN.md`

---

## 1. Layer Structure

The solution follows Clean Architecture with four layers and one test project. The five C# projects map to these layers with strict one-way dependencies enforced through `.csproj` project references.

```
PolicyManagement.Domain          — innermost; no external dependencies
PolicyManagement.Application     — depends on Domain only
PolicyManagement.Infrastructure  — depends on Application and Domain
PolicyManagement.API             — depends on Infrastructure and Application
PolicyManagement.UnitTests       — depends on Application, Infrastructure, and API (for WebApplicationFactory)
```

### Inward-Only Dependency Rule

**Dependencies point inward only. An inner layer never references an outer layer.**

| Layer | May Reference | Must NOT Reference |
|---|---|---|
| Domain | Nothing (SDK only) | Application, Infrastructure, API |
| Application | Domain | Infrastructure, API |
| Infrastructure | Application, Domain | API |
| API | Infrastructure, Application, Domain | (no restriction; this is the composition root) |
| UnitTests | Application, Infrastructure, API | (test-only; no production constraint) |

This rule is enforced structurally: the `.csproj` files contain only the project references listed above. A developer cannot accidentally introduce a forbidden reference without editing a `.csproj` file and breaking the deliberate configuration.

### Enforcement via `.csproj` Project References

**PolicyManagement.Domain.csproj** — no `<ProjectReference>` elements. Only SDK and NuGet packages. Zero NuGet dependencies in this sprint.

**PolicyManagement.Application.csproj**
```xml
<ItemGroup>
  <ProjectReference Include="..\PolicyManagement.Domain\PolicyManagement.Domain.csproj" />
</ItemGroup>
```
No EF Core, ASP.NET Core, or other infrastructure NuGet packages.

**PolicyManagement.Infrastructure.csproj**
```xml
<ItemGroup>
  <ProjectReference Include="..\PolicyManagement.Application\PolicyManagement.Application.csproj" />
  <ProjectReference Include="..\PolicyManagement.Domain\PolicyManagement.Domain.csproj" />
</ItemGroup>
```
NuGet packages: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools`.

**PolicyManagement.API.csproj**
```xml
<ItemGroup>
  <ProjectReference Include="..\PolicyManagement.Infrastructure\PolicyManagement.Infrastructure.csproj" />
  <ProjectReference Include="..\PolicyManagement.Application\PolicyManagement.Application.csproj" />
</ItemGroup>
```
NuGet packages: `Swashbuckle.AspNetCore`, `Serilog.AspNetCore`.

**PolicyManagement.UnitTests.csproj**
```xml
<ItemGroup>
  <ProjectReference Include="..\PolicyManagement.Application\PolicyManagement.Application.csproj" />
  <ProjectReference Include="..\PolicyManagement.Infrastructure\PolicyManagement.Infrastructure.csproj" />
  <ProjectReference Include="..\PolicyManagement.API\PolicyManagement.API.csproj" />
</ItemGroup>
```
NuGet packages: `xunit`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.AspNetCore.Mvc.Testing`.

---

## 2. ASCII Layer Diagram

```
  ┌─────────────────────────────────────────────────────────────┐
  │                    PolicyManagement.API                      │
  │  PoliciesController · Program · Middleware · HealthChecks   │
  └──────────────────────────┬──────────────────────────────────┘
                             │ references
  ┌──────────────────────────▼──────────────────────────────────┐
  │               PolicyManagement.Infrastructure                │
  │   PolicyService · PolicyDbContext · PolicyConfiguration     │
  │              PolicyDataSeeder · Migrations/                  │
  └───────────────┬──────────────────────────────────────────────┘
                  │ references
  ┌───────────────▼──────────────────────────────────────────────┐
  │               PolicyManagement.Application                   │
  │  IPolicyService · PolicyListQuery · PolicyListItemDto        │
  │  PolicyDetailDto · PolicySummaryDto · PaginatedResult<T>     │
  │                    BulkFlagResultDto                         │
  └───────────────┬──────────────────────────────────────────────┘
                  │ references
  ┌───────────────▼──────────────────────────────────────────────┐
  │                  PolicyManagement.Domain                     │
  │         Policy (POCO) · PolicyStatus · LineOfBusiness        │
  │                     Currency (constants)                     │
  └──────────────────────────────────────────────────────────────┘

  Arrows point inward (outward arrows are forbidden).
  PolicyManagement.UnitTests references all layers for testing only.
```

---

## 3. Component Placement

### 3.1 Domain Layer — `PolicyManagement.Domain`

**What belongs here:** Core business concepts that have no dependency on any framework, ORM, HTTP, or infrastructure library. These types would be valid in any host (web, console, batch).

| Component | Path | Reason |
|---|---|---|
| `Policy` entity (POCO) | `Entities/Policy.cs` | Core business object; plain class with auto-properties only; no EF attributes, no validation attributes |
| `PolicyStatus` enum | `Enums/PolicyStatus.cs` | Core domain vocabulary (`Active`, `Expired`, `Pending`, `Cancelled`); belongs in Domain so all layers can use it without a reference chain |
| `LineOfBusiness` enum | `Enums/LineOfBusiness.cs` | Core domain vocabulary (`Property`, `Casualty`, `AandH`, `Marine`); same reasoning as above |
| `Currency` constants class | `Constants/Currency.cs` | Defines the six allowed currency codes as `const string` fields; lives in Domain so the seeder (Infrastructure) and any future write endpoints (API) share one definition without coupling those layers to each other |

**What is forbidden here:** EF Core, ASP.NET Core, `System.ComponentModel.DataAnnotations`, any NuGet package, any reference to outer layers.

### 3.2 Application Layer — `PolicyManagement.Application`

**What belongs here:** The service contract that the API depends on, and the data-transfer objects that flow across layer boundaries. No I/O, no ORM, no HTTP.

| Component | Path | Reason |
|---|---|---|
| `IPolicyService` interface | `Interfaces/IPolicyService.cs` | Defines the contract that controllers depend on; declared here so both the API (consumer) and Infrastructure (implementer) can reference it without a forbidden cross-reference |
| `PolicyListQuery` | `Queries/PolicyListQuery.cs` | Plain parameter object carrying all filter, sort, and pagination fields for the list endpoint; no EF or HTTP types |
| `PolicyListItemDto` | `DTOs/PolicyListItemDto.cs` | Slim projection for list responses; immutable record |
| `PolicyDetailDto` | `DTOs/PolicyDetailDto.cs` | Full projection for the single-policy response; immutable record |
| `PolicySummaryDto` | `DTOs/PolicySummaryDto.cs` | Aggregated summary response; immutable record |
| `PaginatedResult<T>` | `DTOs/PaginatedResult.cs` | Generic wrapper for paginated list responses; immutable record |
| `BulkFlagResultDto` | `DTOs/BulkFlagResultDto.cs` | Response for the bulk-flag operation (`Flagged`, `NotFound` counts); immutable record |

**What is forbidden here:** EF Core, ASP.NET Core, `HttpContext`, concrete database types, any reference to Infrastructure or API projects.

### 3.3 Infrastructure Layer — `PolicyManagement.Infrastructure`

**What belongs here:** All persistence concerns — EF Core context, entity configuration, migrations, data seeder, and the concrete `PolicyService` implementation.

| Component | Path | Reason |
|---|---|---|
| `PolicyService` implementation | `Services/PolicyService.cs` | Implements `IPolicyService`; uses `PolicyDbContext` directly via `IQueryable` composition; lives here because it has a hard dependency on EF Core |
| `PolicyDbContext` | `Persistence/PolicyDbContext.cs` | EF Core `DbContext` subclass; owns the `DbSet<Policy>`; registers all entity configurations |
| EF Core entity configuration | `Persistence/Configurations/PolicyConfiguration.cs` | Fluent API configuration for the `Policy` entity (column types, precision, enum-as-string conversions, unique index); separated from `DbContext` to keep each class single-purpose |
| Data seeder | `Persistence/PolicyDataSeeder.cs` | Inserts 200+ realistic records on first run using `new Random(42)` for determinism; idempotent (checks for existing records before inserting) |
| EF Core migrations | `Persistence/Migrations/` | Auto-generated by `dotnet ef migrations add`; committed to source control; applied on startup via `database.MigrateAsync()` |
| DI registration extension | `DependencyInjection.cs` | `IServiceCollection` extension method `AddInfrastructure()` that registers `PolicyDbContext`, `PolicyService`, and `TimeProvider`; called from `Program.cs` |

**What is forbidden here:** ASP.NET Core `HttpContext`, controller code, any reference to the API project.

### 3.4 API Layer — `PolicyManagement.API`

**What belongs here:** All HTTP concerns — controllers, middleware, health checks, OpenAPI specification, application startup, and configuration wiring.

| Component | Path | Reason |
|---|---|---|
| `PoliciesController` | `Controllers/PoliciesController.cs` | Maps HTTP requests to `IPolicyService` calls; returns correct HTTP status codes; no business logic |
| Request validation | `Controllers/PoliciesController.cs` (data annotations) + `Validation/PolicyListQueryValidator.cs` | Input validation using data annotations on action parameters and a dedicated validator for `PolicyListQuery`; returns 400 Problem Details on failure |
| Global error handler / Problem Details middleware | `Middleware/` or `Program.cs` (`app.UseExceptionHandler`) | Catches unhandled exceptions; maps to 500 Problem Details (RFC 7807); no stack trace in non-Development environments |
| Health checks | `Program.cs` (wired via `AddHealthChecks()`) | Liveness at `GET /health/live`; readiness at `GET /health/ready` with EF Core database connectivity check |
| OpenAPI spec file | `docs/openapi.yaml` | Contract-first OpenAPI 3.x spec; written before controller code; source of truth for all endpoint shapes, parameters, and error responses |
| `Program.cs` | `Program.cs` | Composition root; calls `AddInfrastructure()`, `AddApplication()`, wires middleware pipeline |

**What is forbidden here:** Direct EF Core calls, domain logic, business rules that belong in the service layer.

### 3.5 Test Project — `PolicyManagement.UnitTests`

| Component | Path | Reason |
|---|---|---|
| `PolicyService` unit tests | `Services/PolicyServiceTests.cs` | Tests all four service methods using EF Core InMemory provider; each test creates a fresh in-memory database with a unique name |
| Validation and middleware tests | `Api/ValidationTests.cs` | Uses `WebApplicationFactory<Program>` with InMemory provider; tests 400 and 500 responses |

---

## 4. Folder Tree

```
PolicyManagement/                          — solution root
├── PolicyManagement.sln                   — solution file
├── docker-compose.yml                     — production-like local setup (SQL Server + API)
├── docker-compose.override.yml            — developer overrides (port mappings, volumes)
├── .gitignore                             — excludes bin/, obj/, *.user, Docker volumes
├── README.md                              — zero-context onboarding guide
│
├── docs/
│   └── openapi.yaml                       — contract-first OpenAPI 3.x specification (source of truth)
│
├── PolicyManagement.Domain/
│   ├── PolicyManagement.Domain.csproj     — class library; zero NuGet dependencies
│   ├── Entities/
│   │   └── Policy.cs                      — core POCO with all 14 fields; no framework attributes
│   ├── Enums/
│   │   ├── PolicyStatus.cs                — Active, Expired, Pending, Cancelled
│   │   └── LineOfBusiness.cs              — Property, Casualty, AandH, Marine
│   └── Constants/
│       └── Currency.cs                    — six const string currency codes (USD, SGD, HKD, AUD, JPY, THB)
│
├── PolicyManagement.Application/
│   ├── PolicyManagement.Application.csproj — class library; references Domain only; no EF Core
│   ├── Interfaces/
│   │   └── IPolicyService.cs              — service contract with four async methods
│   ├── Queries/
│   │   └── PolicyListQuery.cs             — parameter object for list endpoint (filters, sort, pagination)
│   └── DTOs/
│       ├── PolicyListItemDto.cs           — slim projection for list responses (immutable record)
│       ├── PolicyDetailDto.cs             — full projection for single-policy response (immutable record)
│       ├── PolicySummaryDto.cs            — aggregated summary with counts and totals (immutable record)
│       ├── PaginatedResult.cs             — generic paginated wrapper (immutable record)
│       └── BulkFlagResultDto.cs           — flagged/notFound counts from bulk-flag operation (immutable record)
│
├── PolicyManagement.Infrastructure/
│   ├── PolicyManagement.Infrastructure.csproj — class library; references Application + Domain; has EF Core NuGet
│   ├── DependencyInjection.cs             — AddInfrastructure() extension method wiring DbContext, PolicyService, TimeProvider
│   ├── Persistence/
│   │   ├── PolicyDbContext.cs             — EF Core DbContext with DbSet<Policy>; applies all configurations
│   │   ├── PolicyDataSeeder.cs            — idempotent seeder; inserts 200+ records with Random(42)
│   │   ├── Configurations/
│   │   │   └── PolicyConfiguration.cs    — fluent API: column types, decimal(18,2), string enums, unique index, timestamps
│   │   └── Migrations/                   — EF Core auto-generated migration files; committed to source control
│   └── Services/
│       └── PolicyService.cs              — IPolicyService implementation; IQueryable composition; ExecuteUpdateAsync for bulk flag
│
├── PolicyManagement.API/
│   ├── PolicyManagement.API.csproj        — ASP.NET Core Web API; references Infrastructure + Application
│   ├── Program.cs                         — composition root; middleware pipeline; DI wiring; health checks
│   ├── appsettings.json                   — non-secret configuration: logging levels, expiring-soon threshold
│   ├── appsettings.Development.json       — development overrides; no secrets
│   ├── Controllers/
│   │   └── PoliciesController.cs         — four action methods; delegates to IPolicyService; no business logic
│   ├── Validation/
│   │   └── PolicyListQueryValidator.cs   — validates sort field against allow-list; validates date range ordering
│   └── Middleware/
│       └── GlobalExceptionHandler.cs     — maps unhandled exceptions to 500 Problem Details (RFC 7807)
│
└── PolicyManagement.UnitTests/
    ├── PolicyManagement.UnitTests.csproj  — xUnit; references Application, Infrastructure, API; EF InMemory NuGet
    ├── Services/
    │   └── PolicyServiceTests.cs         — unit tests for all four PolicyService methods; fresh InMemory DB per test
    └── Api/
        └── ValidationTests.cs            — WebApplicationFactory tests for 400/500 responses
```

---

## 5. Cross-Cutting Concerns

### Logging
- Configured in the API layer via `Program.cs`.
- Uses Serilog (or built-in `ILogger` with JSON console sink) to produce structured JSON output.
- Log enrichment: environment name, request path, duration, status code.
- `ILogger<T>` is injected by the DI container into controllers and services; no static logger calls.
- Sensitive data (connection strings, policy PII in debug logs) must not appear at Information level.

### Error Handling
- Unhandled exceptions are caught by `GlobalExceptionHandler` (registered via `app.UseExceptionHandler`).
- All error responses use RFC 7807 Problem Details (`application/problem+json`).
- `null` returns from `IPolicyService.GetByIdAsync` are handled at the controller level with an explicit 404 Problem Details response — not via exceptions.
- Stack traces are suppressed in non-Development environments.

### Validation
- Request parameter validation uses data annotations on controller action parameters for simple constraints (`[Range]`, `[MaxLength]`).
- The sort field is validated against the allow-list in `PolicyListQueryValidator` before reaching the service; an invalid sort field returns 400 immediately.
- Invalid enum filter strings return 400 via model binding failure.
- Date range inversion (`effectiveDateFrom` after `effectiveDateTo`) is checked in the validator and returns 400.

### Configuration
- Connection string is read from the `ConnectionStrings__DefaultConnection` environment variable; never committed to source control.
- The expiring-soon threshold is read from `appsettings.json` under `PolicySummary:ExpiringSoonThresholdDays` (default 30); overridable without redeployment.
- `dotnet user-secrets` is the documented local development approach for secrets.

### Health Checks
- Liveness: `GET /health/live` — returns 200 as long as the process is running; no external dependency checked.
- Readiness: `GET /health/ready` — includes an EF Core database connectivity check; returns 503 when the database is unreachable.
- Both registered in `Program.cs` via `AddHealthChecks()`.

### Security
- All sort-field input is validated against a typed allow-list (`Expression<Func<Policy, object>>` dictionary) before reaching EF Core. Raw user strings are never passed to `OrderBy`.
- Free-text search uses parameterized `LIKE` queries via EF Core — never string-concatenated SQL.
- Connection strings and secrets are read from environment variables only; no hardcoded values anywhere in the codebase.

---

## 6. Key Abstractions Summary

| Abstraction | Layer | Crosses Boundary To |
|---|---|---|
| `Policy` (POCO) | Domain | Application (via query), Infrastructure (EF Core maps it), API (indirectly via DTOs) |
| `PolicyStatus` (enum) | Domain | All layers |
| `LineOfBusiness` (enum) | Domain | All layers |
| `Currency` (constants) | Domain | Infrastructure (seeder), API (future write endpoints) |
| `IPolicyService` (interface) | Application | API (consumer), Infrastructure (implementer) |
| `PolicyListQuery` (parameter object) | Application | API (constructs it), Infrastructure (PolicyService consumes it) |
| All DTOs | Application | API (serializes them to HTTP responses) |
| `PolicyDbContext` | Infrastructure | Never crosses outward (internal to Infrastructure) |
| `PolicyService` | Infrastructure | Never crosses outward; known to DI container via `IPolicyService` |
