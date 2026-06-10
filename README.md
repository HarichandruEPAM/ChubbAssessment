# Policy Management BFF Service

**Status: COMPLETE — Core implementation done · Bonus features planned (see roadmap)**

Backend-for-Frontend service for Chubb APAC insurance policy data. Aggregates, transforms, and serves policy records to a dashboard. Built with .NET 8, Clean Architecture, SQL Server 2022, and Docker Compose.

---

## Quick Start

```bash
# 1. Copy and populate the environment file
cp .env.example .env
# Edit .env — set SA_PASSWORD and ConnectionStrings__DefaultConnection

# 2. Start the full stack (SQL Server 2022 + API)
docker-compose up

# 3. Access
# Swagger UI:    http://localhost:8080/swagger
# Health live:   http://localhost:8080/health/live
# Health ready:  http://localhost:8080/health/ready
# Policies list: http://localhost:8080/api/v1/policies
```

The database is **migrated and seeded automatically** on first startup — 250 deterministic APAC policy records across all statuses, lines of business, regions, and date ranges.

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/v1/policies` | Paginated list with filtering, sorting, and free-text search |
| `GET` | `/api/v1/policies/{id}` | Single policy by UUID |
| `PATCH` | `/api/v1/policies/flag` | Bulk flag policies for review |
| `GET` | `/api/v1/policies/summary` | Aggregate status counts, premium by LoB, expiring-soon count |

Full contract: [`docs/openapi.yaml`](docs/openapi.yaml)

### List endpoint parameters

| Parameter | Type | Description |
|---|---|---|
| `page` | integer ≥ 1, default `1` | Page number |
| `size` | integer 1–100, default `10` | Page size |
| `sort` | string | Sort field (see allowed values below) |
| `sortDirection` | `asc` \| `desc`, default `desc` | Sort direction |
| `status` | `Active` \| `Expired` \| `Pending` \| `Cancelled` | Filter by status |
| `lineOfBusiness` | `Property` \| `Casualty` \| `AandH` \| `Marine` | Filter by line of business |
| `region` | string | Filter by region name |
| `effectiveDateFrom` | date | Effective date range start |
| `effectiveDateTo` | date | Effective date range end |
| `search` | string | Free-text search across `policyNumber`, `policyholderName`, `underwriter` |

**Allowed sort fields:** `policyNumber`, `policyholderName`, `status`, `lineOfBusiness`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, `updatedAt`

> **A&H wire value:** The Accident & Health line of business is represented as `AandH` on the wire. Send `?lineOfBusiness=AandH` and expect `"lineOfBusiness": "AandH"` in responses.

---

## Architecture

Clean Architecture with four layers. Dependencies point **inward only** — enforced at compile time via `.csproj` project references.

```
PolicyManagement.API
  └─ PolicyManagement.Infrastructure
       └─ PolicyManagement.Application
            └─ PolicyManagement.Domain
```

| Project | Role |
|---|---|
| `PolicyManagement.Domain` | `Policy` entity, enums (`PolicyStatus`, `LineOfBusiness`), `Currency` constants — zero NuGet dependencies |
| `PolicyManagement.Application` | `IPolicyService` interface, DTOs, `SortFields` constants — no infrastructure concerns |
| `PolicyManagement.Infrastructure` | `PolicyDbContext`, `PolicyService`, EF Core migrations, data seeder |
| `PolicyManagement.API` | Controller, validation filter, health checks, Serilog, Swagger/OpenAPI, Problem Details (RFC 7807) |
| `PolicyManagement.UnitTests` | 20 tests: service unit tests + API-layer validation integration tests |

A forbidden cross-layer reference (e.g., Domain referencing Infrastructure) is a **build error**.

---

## Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 8 (C#) |
| Database | SQL Server 2022 via Entity Framework Core 8 (code-first, migrations) |
| Testing | xUnit · FluentAssertions · EF Core InMemory + SQLite in-memory |
| Logging | Serilog (structured JSON, `CompactJsonFormatter`) |
| API docs | Swashbuckle (OpenAPI 3.x, XML comments) |
| Container | Docker Compose |
| Error format | RFC 7807 Problem Details on all 4xx/5xx responses |

---

## Running Tests

```bash
dotnet test PolicyManagement.sln
```

**Current status: 20 tests · 20 passed · 0 failed**

| Suite | Provider | What is tested |
|---|---|---|
| `PolicyServiceTests` (12 tests) | EF Core InMemory + SQLite in-memory | `ListAsync` filtering/sorting/pagination, `GetByIdAsync`, `BulkFlagAsync`, `GetSummaryAsync` |
| `ValidationTests` (7 tests) | `WebApplicationFactory` (InMemory DB) | 400/404 response shapes, sort field validation, date range validation |
| Placeholder | — | 1 scaffold placeholder |

> `BulkFlagAsync` tests use SQLite in-memory because EF Core's InMemory provider does not support `ExecuteUpdateAsync` (bulk update).

---

## Configuration

All secrets come from environment variables. **No secret is committed to source control.**

| Environment variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `SA_PASSWORD` | SQL Server SA password (Docker Compose only) |

Non-secret configuration in `appsettings.json`:

| Key | Default | Description |
|---|---|---|
| `PolicySummary:ExpiringSoonThresholdDays` | `30` | Look-ahead days for the expiring-soon count in `/summary` |

Local development: use a `.env` file (gitignored). See `.env.example` for the required keys.

---

## Security

| Control | Implementation |
|---|---|
| Secrets | All via environment variables — zero secrets in any committed file |
| SQL injection | Sort fields validated against a compile-time typed allow-list (`Expression<Func<Policy, object>>`); all queries use EF Core parameterisation |
| Input validation | `page`/`size` bounded by data annotations; `sort`, `sortDirection`, `status`, `lineOfBusiness` validated against allow-lists at the API layer before reaching the service; date range order checked |
| Error responses | RFC 7807 Problem Details on all errors; stack traces suppressed outside Development |
| Health checks | `/health/live` (process-up) and `/health/ready` (DB connectivity) |
| Pre-commit hook | `.claude/hooks/check-secrets.ps1` — blocks Write/Edit tool calls that contain hardcoded secrets, passwords, connection strings, or API keys |

Pre-production items explicitly out of scope for this assessment:
- JWT Bearer authentication (Azure AD / Entra ID)
- `[MaxLength]` on `BulkFlagRequest.PolicyIds` and the `search` parameter
- Remove `TrustServerCertificate=True` from `.env.example` before staging/production

---

## Build Commands

```bash
# Build solution
dotnet build

# Run all tests
dotnet test

# Start infrastructure services
docker-compose up -d

# Add EF Core migration
dotnet ef migrations add <MigrationName> --project src/Infrastructure --startup-project src/Api

# Apply migrations manually
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

---

## Bonus Features (Planned)

The following two bonus items from the assessment spec are **designed but not yet implemented**. The architecture, CLAUDE.md guidance, and agent tooling are all ready for them.

### Bonus 1 — Summary statistics cache with invalidation

**Goal:** Eliminate repeated aggregation queries on `GET /api/v1/policies/summary`, which executes three grouped SQL queries on every request.

**Design:**
- Pattern: **cache-aside** — check cache first; on miss, query DB and populate cache; on mutation that affects summary data, invalidate.
- Abstraction: `IDistributedCache` (Redis in production, in-memory fallback in development when Redis is not configured).
- Implementation: `CachedPolicyService` decorator wrapping `IPolicyService`. Only `GetSummaryAsync` touches the cache; all other methods pass through. The controller sees no change.
- Cache key: `"policy:summary"` · TTL: configurable via `Cache:SummaryTtlSeconds` (default 300 s).
- Invalidation trigger: the Kafka status-changed consumer (see Bonus 2) calls `cache.RemoveAsync("policy:summary")` after a successful status update — the only mutation that changes summary statistics.
- Infrastructure addition: Redis service in `docker-compose.yml`, connection string via `ConnectionStrings__Redis` environment variable.

```
Request → CachedPolicyService → [cache hit] return JSON
                               → [cache miss] PolicyService → DB → store in Redis → return
```

### Bonus 2 — Kafka event producer and idempotent consumer

**Goal:** Emit domain events when policies are flagged, and consume external status-change events to keep the database in sync — without double-processing.

**Producer — flag events:**
- Trigger: `BulkFlagAsync` publishes a `PolicyFlaggedEvent` (policy IDs + timestamp) to topic `insurance.policies.flagged` after the DB update succeeds.
- Interface: `IPolicyEventPublisher` in the Application layer. Infrastructure provides `KafkaPolicyEventPublisher`.
- Config: `enable.idempotence=true`, `acks=all` for exactly-once delivery semantics on the broker.

**Consumer — status changes:**
- Listens on topic `insurance.policies.status-changed` for `PolicyStatusChangedEvent` (`{ PolicyId, NewStatus }`).
- **Idempotency:** before processing, checks a `ProcessedMessages` table (`MessageId` + `Topic` unique index). If the message ID is already present, skips processing.
- **Atomicity:** DB status update + `ProcessedMessages` insert run inside a single transaction.
- **Cache invalidation:** if the status update changes rows, removes `"policy:summary"` from cache.
- **Dead-letter:** deserialization errors are produced to `insurance.policies.status-changed.dlq` with a `dlq-reason` header, then the offset is committed — the consumer never blocks on bad messages.
- Offset commits happen only **after** successful processing (manual commit, `enable.auto.commit=false`).
- Implemented as a `BackgroundService` with an outer retry loop (backs off 10 s on unexpected error) so the application stays up if Kafka is temporarily unavailable.

**Topic naming convention:** `<domain>.<entity>.<event>` — e.g., `insurance.policies.flagged`, `insurance.policies.status-changed`, `insurance.policies.status-changed.dlq`.

**Infrastructure additions:**
- `Confluent.Kafka` NuGet package in Infrastructure.
- Bitnami Kafka 3.7 (KRaft mode, no Zookeeper) in `docker-compose.yml`.
- All broker addresses, topic names, and group IDs from `IConfiguration` via `KafkaOptions`.
- `ProcessedMessages` table added via EF Core migration.

```
BulkFlagAsync → DB update → KafkaPolicyEventPublisher → insurance.policies.flagged
                                                                     ↓
External system → insurance.policies.status-changed → PolicyStatusChangedConsumer
                                                         → check ProcessedMessages (idempotency)
                                                         → update Policy.Status (transaction)
                                                         → insert ProcessedMessage
                                                         → invalidate summary cache
                                                         → commit offset
```

---

## MCP-Powered Test Generation (Planned)

[Model Context Protocol (MCP)](https://modelcontextprotocol.io) servers expose live data sources and tools to AI agents. Wiring an MCP server into this project's Claude Code workflow would let the tester agent generate and run a **complete, realistic test suite automatically** — without hand-coding each case.

### What MCP enables here

| Capability | How it works |
|---|---|
| **OpenAPI-driven test generation** | An MCP server reads `docs/openapi.yaml` at generation time; the tester agent sees the live contract and generates a test for every endpoint, every parameter combination, and every documented error response — not just the ones a developer remembers to cover |
| **Database-aware integration tests** | An MCP server connects to the running SQL Server instance; the agent can query actual seed data (250 APAC policies) to build assertions against real record counts, date ranges, and premium values instead of hard-coded magic numbers |
| **Continuous contract verification** | On every PR, a CI step spins up the MCP server, the tester agent reads the current spec, re-generates integration tests, and runs them — any drift between code and contract fails the build before merge |
| **Idempotency verification for Kafka consumer** | An MCP server publishes duplicate messages to the test Kafka topic; the tester agent asserts `ProcessedMessages` count stays at 1 and the policy status updates exactly once |

### Implementation plan

**Step 1 — OpenAPI MCP server**

Create a lightweight MCP server (`mcp-servers/openapi-reader/`) that exposes one resource:

```
resource: openapi://spec
→ returns the full parsed openapi.yaml as a structured object
```

The tester agent reads this resource and generates:
- One happy-path test per endpoint
- One test per documented 4xx error code
- Boundary tests for every numeric parameter (`page`, `size`, `premiumAmount`)
- Enum exhaustion tests for `status` and `lineOfBusiness`

**Step 2 — Database MCP server**

Create a second MCP server (`mcp-servers/db-inspector/`) that exposes:

```
resource: db://policies/count?status=Active
resource: db://policies/sample?n=5
tool:     db_query(sql: string) → rows   [read-only, parameterised only]
```

The tester agent uses these resources to:
- Assert `GET /api/v1/policies?status=Active` returns exactly the count the DB reports
- Assert `/summary` premiumByLob values match the DB aggregation
- Assert the seeder produced the expected 250 records on first boot

**Step 3 — Integration test project**

Add `PolicyManagement.IntegrationTests` (separate from `UnitTests`):

```
PolicyManagement.IntegrationTests/
  Api/
    PoliciesListIntegrationTests.cs   ← filter/sort/pagination against real DB
    PolicyDetailIntegrationTests.cs   ← 200 vs 404 with seed data IDs
    BulkFlagIntegrationTests.cs       ← verifies DB state post-flag
    SummaryIntegrationTests.cs        ← aggregation values vs DB counts
  Kafka/
    StatusChangedConsumerTests.cs     ← publishes to test topic, asserts DB update
    IdempotencyTests.cs               ← duplicate message, asserts single DB row
  Infrastructure/
    CacheIntegrationTests.cs          ← summary hit/miss/invalidate cycle
```

Tests use `WebApplicationFactory<Program>` with a real SQL Server container (Testcontainers) — no InMemory shortcuts that hide SQL compatibility issues.

**Step 4 — CI wiring**

```yaml
# .github/workflows/integration-tests.yml
- name: Start MCP servers
  run: docker-compose -f docker-compose.mcp.yml up -d

- name: Run integration tests via tester agent
  run: claude --agent tester "Read openapi://spec and db://policies/count. Generate and run all integration tests."

- name: Upload test report
  uses: actions/upload-artifact@v4
  with:
    path: test-results/
```

### Value delivered

- **Zero test-case amnesia** — the agent reads the spec, not memory; every documented behaviour gets a test
- **Living documentation** — tests are regenerated from the current spec on every run, so they drift with the API naturally
- **Faster PR review** — reviewers see a generated test report alongside every diff; no "does this break anything?" guesswork
- **Kafka confidence** — idempotency is notoriously hard to test manually; automated duplicate-message tests give deterministic coverage

---

## Future Feature Roadmap

Features that would make this service production-ready and genuinely valuable to the Chubb APAC dashboard team. Ordered by impact.

### Priority 1 — Security & Compliance (must-have for production)

**Authentication and authorization**
- JWT Bearer authentication via Azure AD / Entra ID (`Microsoft.Identity.Web`)
- Role-based access control: `underwriter` can read and flag; `manager` can read summary; `admin` can do everything
- All endpoints require a valid token; anonymous access returns 401
- Claim-based filtering: underwriters see only their own policies (`WHERE Underwriter = @claimName`)

**Audit trail**
- Every write operation (`BulkFlagAsync`, future status changes) writes an immutable `PolicyAuditLog` row: `PolicyId`, `Field`, `OldValue`, `NewValue`, `ChangedBy`, `ChangedAt`
- `GET /api/v1/policies/{id}/audit` returns the full history for a policy
- Critical for regulatory compliance in insurance — every state change must be attributable to a person

**Rate limiting**
- `Microsoft.AspNetCore.RateLimiting` with a sliding window: 100 requests / 60 s per client IP
- Separate limit for the `BulkFlagAsync` endpoint (write-heavy): 10 requests / 60 s
- Returns 429 with `Retry-After` header and Problem Details body

### Priority 2 — Operational Readiness

**Distributed tracing**
- Add OpenTelemetry (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`)
- Export traces to Azure Application Insights or Jaeger
- Every request gets a `TraceId` propagated through the Kafka messages so a flag event can be traced from HTTP request → DB update → Kafka produce → consumer → DB update end-to-end

**Structured alerting**
- Prometheus metrics endpoint (`/metrics`) via `prometheus-net.AspNetCore`
- Expose: request rate, error rate, p50/p95/p99 latency, DB query duration, Kafka consumer lag, cache hit/miss ratio
- Grafana dashboard template shipped in `infra/grafana/`

**Graceful shutdown**
- `IHostApplicationLifetime` hook flushes Kafka producer and commits in-flight consumer offsets before the process exits
- Kubernetes `terminationGracePeriodSeconds: 30` aligned with the flush timeout

**Testcontainers-based integration tests**
- Replace `WebApplicationFactory` + InMemory DB with real SQL Server container (`Testcontainers.MsSql`)
- Ensures EF Core migrations, indexes, and SQL Server-specific behaviour are all exercised in CI
- One `DockerFixture` class manages container lifecycle across the test suite

### Priority 3 — Product Features (dashboard value)

**Policy lifecycle management**
- `POST /api/v1/policies/{id}/renew` — creates a new policy record with a new term, links to the original via `RenewedFromId`, publishes a `PolicyRenewed` Kafka event
- `POST /api/v1/policies/{id}/cancel` — sets status to `Cancelled`, records `CancellationReason`, publishes `PolicyCancelled`
- `POST /api/v1/policies/{id}/status` — manual status override for managers, writes audit log entry

**Real-time dashboard updates (SignalR)**
- `PolicyHub` pushes status-change notifications to connected dashboard clients the moment the Kafka consumer processes a message
- Client subscribes to `policy/{id}/updates` or `summary/updates`
- Eliminates the need for the dashboard to poll `/summary` — the cache TTL becomes a safety net, not the primary freshness mechanism

**Advanced filtering and search**
- Full-text search via SQL Server `FREETEXT` or Azure Cognitive Search for typo-tolerant policyholder name lookup
- Saved filter sets: users persist named filter combinations (`my-expiring-this-month`) and recall them by ID
- `GET /api/v1/policies/export?format=csv` — streams a CSV of the current filtered result set using `CsvHelper`; respects all active filter parameters; returns 202 + a job ID for large exports

**Analytics endpoints**
- `GET /api/v1/analytics/premium-trend?months=12` — monthly premium totals for the past N months, grouped by line of business
- `GET /api/v1/analytics/expiry-forecast?days=90` — policies expiring in the next N days, grouped by week and region, for renewal pipeline planning
- `GET /api/v1/analytics/flag-rate` — percentage of policies flagged per underwriter over the last 30 days

**Bulk operations**
- `PATCH /api/v1/policies/status` — bulk status update for a list of IDs (manager role only), writes audit log for each
- `POST /api/v1/policies/import` — accepts a CSV of new policies, validates all rows before committing any, returns a report of rows accepted / rejected with reasons

### Priority 4 — Infrastructure Maturity

**Multi-region read replicas**
- Route read endpoints (`GET /policies`, `GET /policies/{id}`, `GET /summary`) to an Azure SQL read replica
- Write endpoints stay on the primary; EF Core `UseQuerySplittingBehavior` and `AsNoTracking()` enforced on all read paths
- Reduces primary DB load for the high-volume list endpoint

**Secret rotation without restarts**
- Replace `IConfiguration` secret reads at startup with `Azure.Extensions.AspNetCore.Configuration.Secrets` (Azure Key Vault provider)
- Secrets refresh on a 5-minute poll cycle — a rotated DB password or Kafka credential is picked up without redeployment

**Database partitioning**
- Partition the `Policies` table by `Region` (SQL Server partition function) once row count exceeds 10M
- Queries filtered by region hit a single partition; aggregation queries parallelise across partitions
- Partition scheme documented as ADR-011

---

## Developer Tooling

This repository uses Claude Code with a structured multi-agent workflow. The `.claude/` directory contains:

| Path | Purpose |
|---|---|
| `.claude/settings.json` | Project-level permissions (deny destructive shell commands) and PreToolUse hooks |
| `.claude/hooks/check-secrets.ps1` | Real-time secret detection — blocks any file write that matches hardcoded credential patterns before it reaches disk |
| `.claude/agents/orchestrator.md` | Drives the full 8-agent pipeline: planner → architect → implementer → tester → reviewer → corrector → security → documenter |
| `.claude/agents/*.md` | Specialised agents — each has a scoped `tools:` allowlist (read-only agents cannot write files; implementer/corrector have shell access) |

**Agent tool allowlists:**

| Agent | Tools | Rationale |
|---|---|---|
| planner, reviewer, security | `Read, Grep, Glob` | Analysis only — no file writes |
| architect | `Read, Write, Grep, Glob` | Writes architecture docs and ADRs, but no shell |
| documenter | `Read, Write, Edit, Grep, Glob` | Updates README and journal, no shell |
| implementer, corrector, tester | `Read, Write, Edit, Bash, Grep, Glob` | Full code write + build/test execution |
| orchestrator | `Read, Write, Bash, Grep, Glob, Agent` | Pipeline coordination and agent spawning |

---

## Key Documents

| Document | Path |
|---|---|
| OpenAPI specification | [`docs/openapi.yaml`](docs/openapi.yaml) |
| AI working journal | [`docs/AI_WORKING_JOURNAL.md`](docs/AI_WORKING_JOURNAL.md) |
| Sprint plan + risk register | [`.docs/PLAN.md`](.docs/PLAN.md) |
| Architecture document | [`.docs/ARCHITECTURE.md`](.docs/ARCHITECTURE.md) |
| Architecture Decision Records | [`.docs/adr/`](.docs/adr/) |
| Security scan report | [`.docs/SECURITY-SCAN.md`](.docs/SECURITY-SCAN.md) |
| Final summary | [`.docs/FINAL-SUMMARY.md`](.docs/FINAL-SUMMARY.md) |

---

## Build Status

```
dotnet build PolicyManagement.sln  →  0 errors, 0 warnings
dotnet test PolicyManagement.sln   →  20 tests, 20 passed, 0 failed
```

## What Would Come Next (from PLAN.md "What I Would Do Next")
- Testcontainers integration tests against real SQL Server 2022 (migration correctness, unique index enforcement)
- Distributed caching for `GET /api/v1/policies/summary` with Redis + TTL invalidation on BulkFlag
- Kafka producer for `PolicyFlaggedEvent`; consumer for status change events (idempotent)
- Authentication (JWT Bearer + Azure AD / Entra ID for Chubb OneHub production)
- `[MaxLength]` on `BulkFlagRequest.PolicyIds` and `Search` parameter (low-severity security findings)

