# Policy Management BFF Service

**Status: COMPLETE**
Backend-for-Frontend service for Chubb APAC insurance policy data. All 6 implementation phases complete, all gates passed.

---

## Quick Start

```bash
# 1. Copy and populate the environment file
cp .env.example .env
# Edit .env: set SA_PASSWORD and ConnectionStrings__DefaultConnection

# 2. Start the full stack (SQL Server 2022 + API)
docker-compose up

# 3. Access the API
# Swagger UI:     http://localhost:8080/swagger
# Health live:    http://localhost:8080/health/live
# Health ready:   http://localhost:8080/health/ready
# Policies list:  http://localhost:8080/api/v1/policies
```

The database is migrated and seeded automatically on first startup (250 deterministic APAC policy records).

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/api/v1/policies` | Paginated list with filtering, sorting, free-text search |
| GET | `/api/v1/policies/{id}` | Single policy by UUID |
| PATCH | `/api/v1/policies/flag` | Bulk flag policies for review |
| GET | `/api/v1/policies/summary` | Aggregate counts and premium totals |

Full contract: [`docs/openapi.yaml`](docs/openapi.yaml)

### List endpoint parameters

| Parameter | Type | Description |
|---|---|---|
| `page` | integer (≥1, default 1) | Page number |
| `size` | integer (1–100, default 10) | Page size |
| `sort` | string | Sort field (see allowed values below) |
| `sortDirection` | `asc` or `desc` (default `desc`) | Sort direction |
| `status` | `Active`, `Expired`, `Pending`, `Cancelled` | Filter by status |
| `lineOfBusiness` | `Property`, `Casualty`, `AandH`, `Marine` | Filter by LOB |
| `region` | string | Filter by region |
| `effectiveDateFrom` | date | Effective date range start |
| `effectiveDateTo` | date | Effective date range end |
| `search` | string | Free-text search across policyNumber, policyholderName, underwriter |

**Allowed sort fields:** `policyNumber`, `policyholderName`, `status`, `lineOfBusiness`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, `updatedAt`

**Note on `lineOfBusiness` wire value:** The `A&H` (Accident & Health) line of business is represented as `AandH` on the wire. Send `?lineOfBusiness=AandH` and expect `"lineOfBusiness": "AandH"` in responses.

---

## Architecture

Clean Architecture with four layers and strict inward-only dependencies:

```
PolicyManagement.API
  └─ PolicyManagement.Infrastructure
       └─ PolicyManagement.Application
            └─ PolicyManagement.Domain
```

| Project | Role |
|---|---|
| `PolicyManagement.Domain` | Policy POCO, enums, Currency constants — zero NuGet dependencies |
| `PolicyManagement.Application` | `IPolicyService` interface, DTOs, `SortFields` constants |
| `PolicyManagement.Infrastructure` | `PolicyDbContext`, `PolicyService`, seeder, EF Core migrations |
| `PolicyManagement.API` | Controller, validation filter, health checks, Serilog, Swagger, Problem Details |
| `PolicyManagement.UnitTests` | 20 tests: service unit tests + API integration tests |

Dependencies enforced at compile time via `.csproj` project references. A forbidden cross-layer reference is a build error.

---

## Running Tests

```bash
dotnet test PolicyManagement.sln
```

**Test status: 20 tests, 20 passed, 0 failed**

| Suite | Provider | Coverage |
|---|---|---|
| `PolicyServiceTests` (12 tests) | EF Core InMemory + SQLite in-memory | ListAsync, GetByIdAsync, BulkFlagAsync, GetSummaryAsync |
| `ValidationTests` (7 tests) | WebApplicationFactory (InMemory) | 400/404 response shapes |
| Placeholder | — | 1 placeholder |

`BulkFlagAsync` tests use SQLite in-memory (not EF Core InMemory) because EF Core's InMemory provider does not support `ExecuteUpdateAsync`.

---

## Configuration

All secrets are read from environment variables. No secrets are committed to source control.

| Variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `SA_PASSWORD` | SQL Server SA password (Docker Compose only) |

Non-secret configuration lives in `appsettings.json`:

| Key | Default | Description |
|---|---|---|
| `PolicySummary:ExpiringSoonThresholdDays` | `30` | Days ahead for expiring-soon count in summary |

Local development: use `dotnet user-secrets` or a `.env` file (gitignored).

---

## Security

- **Secrets:** All via environment variables — zero secrets in any committed file
- **SQL injection:** Sort fields are validated against a compile-time typed allow-list (`Expression<Func<Policy, object>>`); all queries use EF Core parameterisation
- **Input validation:** Page/size bounded by data annotations; sort field, sort direction, status, and lineOfBusiness validated against allow-lists before reaching the service; date range ordering checked
- **Error handling:** RFC 7807 Problem Details on all error responses; stack traces suppressed in non-Development environments
- **Health checks:** `/health/live` (liveness) and `/health/ready` (database connectivity)

Pre-production requirements (explicitly out of scope for this assessment):
- JWT Bearer authentication (Azure AD / Entra ID)
- `[MaxLength]` on `BulkFlagRequest.PolicyIds` and `Search` parameter
- Remove `TrustServerCertificate=True` from `.env.example` before staging/production use

---

## Key Documents

| Document | Path |
|---|---|
| OpenAPI specification | `docs/openapi.yaml` |
| AI working journal | `docs/AI_WORKING_JOURNAL.md` |
| Sprint plan + risk register | `.docs/PLAN.md` |
| Architecture document | `.docs/ARCHITECTURE.md` |
| Architecture Decision Records | `.docs/adr/001-010.md` |
| Security scan | `.docs/SECURITY-SCAN.md` |
| Final summary | `.docs/FINAL-SUMMARY.md` |

---

## Build Status

```
dotnet build PolicyManagement.sln   →  0 errors, 0 warnings
dotnet test PolicyManagement.sln    →  20 tests, 20 passed, 0 failed
```
