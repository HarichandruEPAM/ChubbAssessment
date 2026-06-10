# Engineering Constraints (Global)

## Purpose
These constraints apply to all work in this repository. They are
project-agnostic engineering standards. Task-specific requirements
live separately in .specs/requirements.md.

## Stack
- Runtime: .NET 8 (C#)
- Database: SQL Server 2022 via Entity Framework Core
- Testing: xUnit with FluentAssertions and Moq
- Containerization: Docker Compose
- API specification: OpenAPI 3.x (Swashbuckle)

## Commands
```bash
# Build solution
dotnet build

# Run all tests
dotnet test

# Start all infrastructure services (SQL Server, Redis, Kafka)
docker-compose up -d

# Add EF Core migration
dotnet ef migrations add <MigrationName> --project src/Infrastructure --startup-project src/Api

# Apply pending migrations
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

## Architecture
- Follow Clean Architecture: Domain, Application, Infrastructure, API layers
- Dependencies point inward only; inner layers never reference outer layers
- No infrastructure concerns (database, HTTP) leak into Domain or Application

## Code Quality
- Apply SOLID and DRY principles
- No method longer than ~40 lines; extract when longer
- Meaningful names; no abbreviations except well-known ones
- Prefer composition over inheritance

## Async and Data Access
- Always use native async methods for I/O (database, network, file)
- Never wrap synchronous work in Task.Run to fake async
- Use IQueryable composition for database filtering; defer execution

## Security (non-negotiable)
- Never hardcode secrets, passwords, API keys, or connection strings in code
- Read all secrets from configuration or environment variables
- Validate and sanitize all external inputs
- Use parameterized queries only; never string-concatenate SQL

## Testing
- All business logic must have unit tests
- Tests must be independent and isolated; no shared mutable state
- Use the Arrange-Act-Assert pattern

## Decision Discipline
- Every significant architectural decision must have written reasoning
- When choosing between options, document why the alternative was rejected
- Prefer the choice that matches the stated production environment

## Documentation
- Every stage of work ends with a short pros/cons reflection
- Keep a running AI working journal of what was accepted, changed, or overridden

## Caching Guidance
Implement cache-aside pattern: check cache first; on miss, load from database and populate cache; on write, invalidate affected cache entries.

- Use `IDistributedCache` abstraction (Redis-backed in production, in-memory in tests).
- Cache keys must be deterministic and namespaced: `<entity>:<id>` or `<entity>:list:<filter-hash>`.
- Set explicit TTLs on every cache entry — no indefinite caching.
- Invalidation strategy: invalidate by key on mutation. No silent expiry-only invalidation.
- Cache only at the Application layer via a cache-decorated repository — never in Domain or directly in controllers.
- Never cache write operations or authentication tokens.

## Kafka Guidance
Use Confluent.Kafka client. Configure the producer and consumer in Infrastructure; expose only interfaces to Application.

- Producer: enable idempotence (`enable.idempotence=true`), set `acks=all`, use transactional outbox pattern for at-least-once delivery.
- Consumer: commit offsets only after successful processing; handle `ConsumeException` and log without crashing the consumer loop.
- Idempotent consumer: persist a processed-message-id in the database; skip processing if the ID is already present.
- Topic naming convention: `<domain>.<entity>.<event>` (e.g., `insurance.claims.submitted`).
- Deserialization errors must be dead-lettered to a `<topic>.dlq` topic — never silently dropped.
- All broker addresses, topic names, and group IDs must come from `IConfiguration` — never hardcoded.
