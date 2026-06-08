# ADR-003: PolicyService Uses DbContext Directly — No Repository Abstraction

**Status:** Accepted
**Date:** 2026-06-08

## Context

A common Clean Architecture pattern is to introduce an `IRepository<T>` or `IPolicyRepository` interface in the Application layer, implemented in Infrastructure by an EF Core repository class. The intent is to decouple Application from EF Core. In practice, this project uses EF Core and `IQueryable` composition extensively: the list endpoint chains multiple `Where`, `OrderBy`, `Skip`, and `Take` calls before projecting to a DTO in a single database round-trip. The service also uses `ExecuteUpdateAsync` for bulk updates, which is an EF Core-specific API with no natural generic repository equivalent.

The `PolicyService` in Infrastructure already implements `IPolicyService`, which is defined in Application and contains zero EF Core types. This provides the cross-layer decoupling that a repository would otherwise serve.

## Decision

`PolicyService` takes `PolicyDbContext` directly via constructor injection. There is no `IRepository<T>`, no `IPolicyRepository`, and no repository class. All data access is written using EF Core `IQueryable` composition and native async EF Core methods directly in `PolicyService`.

The Application-level service interface (`IPolicyService`) serves as the abstraction boundary. Controllers depend on `IPolicyService`, not on any EF Core type.

## Consequences

### Positive
- `IQueryable` composition across `PolicyService` methods produces a single optimized SQL query per operation; a generic repository cannot expose `IQueryable` without leaking EF Core types into the Application layer.
- `ExecuteUpdateAsync` for bulk flagging is expressible directly; a generic repository would need a special-case method (e.g., `BulkUpdateAsync`) that either leaks EF Core expressions or loses the no-load benefit.
- One fewer abstraction layer means one fewer class per operation to navigate, write, and test.
- Unit tests use EF Core InMemory provider against the real `PolicyDbContext`, which exercises the actual query composition without requiring a repository mock.

### Negative / Trade-offs
- `PolicyService` is coupled to EF Core. If the persistence technology changes (e.g., to Dapper or a NoSQL store), `PolicyService` must be rewritten rather than replaced by a different `IRepository` implementation. This is an acceptable trade-off given that EF Core with SQL Server is the stated production technology.
- Tests that use the InMemory provider test the service and the context together; there is no seam to test the service with a mock persistence layer independently of EF Core. For this sprint scope this is intentional and acceptable.

## Alternatives Considered

### Alternative: Generic IRepository<T>
**Rejected because:** A generic `IRepository<T>` with `Add`, `GetById`, `List`, and `SaveChanges` methods cannot express the `IQueryable` composition needed for the list endpoint (dynamic filtering, sorting, full-text search, pagination in a single query) without either exposing `IQueryable<T>` — which leaks EF Core into the Application layer — or requiring a repository method per filter combination, which is impractical. The abstraction provides the illusion of persistence independence while in practice being shaped entirely around EF Core's capabilities.

### Alternative: Specific IPolicyRepository
**Rejected because:** A specific `IPolicyRepository` interface in the Application layer with methods like `ListAsync(PolicyListQuery query)` and `BulkFlagAsync(IReadOnlyList<Guid> ids)` would have the exact same signatures as `IPolicyService`. This creates two interfaces, two implementations, and a delegation chain (`PolicyService` → `IPolicyRepository` → `PolicyRepository`) for no behavioural difference. Since `IPolicyService` already provides the Application-layer abstraction, adding `IPolicyRepository` is redundant indirection.
