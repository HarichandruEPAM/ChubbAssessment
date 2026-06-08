# ADR-001: Clean Architecture 4-Layer Separation

**Status:** Accepted
**Date:** 2026-06-08

## Context

The Policy Management BFF Service must be maintainable, testable in isolation, and free from framework lock-in in its core logic. `CLAUDE.md` mandates Clean Architecture with inward-only dependencies and no infrastructure concerns leaking into Domain or Application. The system has four natural concerns: core business concepts, the service contract, persistence, and HTTP delivery. These concerns have different rates of change and different dependency needs. Without an enforced boundary, EF Core and ASP.NET Core types tend to creep into business logic over time, making unit testing difficult and framework migration expensive.

## Decision

The solution is structured as four C# projects mapping to Clean Architecture layers:

- `PolicyManagement.Domain` — core entities and enums; zero external dependencies.
- `PolicyManagement.Application` — service interface (`IPolicyService`) and all DTOs; references Domain only.
- `PolicyManagement.Infrastructure` — EF Core `DbContext`, entity configuration, migrations, data seeder, and `PolicyService` implementation; references Application and Domain.
- `PolicyManagement.API` — ASP.NET Core controllers, middleware, health checks, and the composition root; references Infrastructure and Application.

Dependency direction is enforced structurally through `.csproj` project references. No inner-layer project file contains a reference to an outer-layer project. Violations are caught at compile time — a forbidden import does not compile.

## Consequences

### Positive
- Domain and Application layers have zero framework dependencies; they can be unit tested with plain .NET objects and no test doubles for the ORM.
- `IPolicyService` decouples the API from the concrete persistence implementation; the service can be replaced (e.g., with a mock or a different data source) without touching any controller code.
- The layer separation makes the intended structure visible and navigable to new developers without requiring documentation.
- Swapping the web framework or the ORM is structurally contained to the outermost layers.

### Negative / Trade-offs
- Five projects instead of one increases solution file management overhead (five `.csproj` files, references to keep in sync).
- A simple read operation (e.g., fetching one policy by ID) must cross all four layers before returning a response, introducing more files to navigate than a flat structure would require.
- The constraint that Application must not reference EF Core means projections (`Select`) from `IQueryable` to DTOs must happen in Infrastructure (`PolicyService`), which is less intuitive than projecting in the calling layer.

## Alternatives Considered

### Alternative: Vertical Slice Architecture
**Rejected because:** Vertical slicing groups all code for one feature (handler, query, DTO, validation) into a single folder. This improves feature cohesion but removes the shared-contract boundary that `IPolicyService` provides. With four endpoints and shared domain vocabulary (`Policy`, `PolicyStatus`), the cross-cutting benefit of a single Domain/Application layer outweighs the feature-isolation benefit of vertical slices at this scope.

### Alternative: Flat Single-Project Structure
**Rejected because:** A single project with folders (Domain/, Application/, Infrastructure/, Api/) does not enforce the inward-only dependency rule at compile time. Any class can reference any other class, so violations accumulate silently. The requirement for isolated unit tests of business logic — without instantiating a database — is much harder to satisfy without a physical project boundary separating the service interface from its EF Core implementation.
