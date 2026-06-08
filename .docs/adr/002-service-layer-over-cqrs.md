# ADR-002: Service Layer (IPolicyService) Over CQRS/MediatR for This Sprint Scope

**Status:** Accepted
**Date:** 2026-06-08

## Context

The API exposes four operations: list policies with filtering/pagination, get a single policy, bulk-flag policies for review, and retrieve a summary. A common pattern for .NET APIs at this scale is CQRS implemented via MediatR, where each operation becomes a `IRequest<T>` handler and controllers dispatch to `IMediator.Send()`. The alternative is a focused service interface (`IPolicyService`) with one method per operation, implemented as a concrete class, with controllers calling the interface directly.

`CLAUDE.md` requires SOLID principles and prefers the choice that reduces ceremony without sacrificing correctness or testability.

## Decision

`IPolicyService` is defined in the Application layer with four async methods. `PolicyService` in Infrastructure implements this interface. Controllers receive `IPolicyService` via constructor injection and call methods directly. No MediatR, no command/query objects beyond `PolicyListQuery`, and no mediator pipeline.

## Consequences

### Positive
- `IPolicyService` is directly injectable in unit tests — replace with a mock or an in-memory implementation without any MediatR pipeline setup.
- Four methods on a single interface are the complete contract; a new developer can read the interface and understand every operation the service supports without navigating a `Handlers/` directory tree.
- No pipeline behaviors, no `IRequestHandler<,>` registrations, no mediator package to configure — the composition root stays simple.
- The interface can be evolved (add a method, change a return type) with a single file change and its implementation; in CQRS each handler is its own class.

### Negative / Trade-offs
- `IPolicyService` grows as new endpoints are added; a large interface violates ISP. At four methods this is not a practical problem, but teams with many endpoints find CQRS cleaner at scale.
- Cross-cutting pipeline behaviors (logging, caching, validation) are easier to add in MediatR via `IPipelineBehavior<,>`. With a service interface, cross-cutting logic is either duplicated per method or extracted to a decorator — both require more deliberate design work.
- This decision should be revisited if the service expands beyond approximately ten operations or if a cross-cutting pipeline (e.g., caching with invalidation, audit logging per command) becomes a requirement.

## Alternatives Considered

### Alternative: MediatR with Commands and Queries
**Rejected because:** At four query/command types, MediatR adds four handler classes, four request objects, and a mediator pipeline with no functional benefit over a typed service interface. The benefit of MediatR — decoupled dispatch and pipeline behaviors — does not materialise until there are enough handlers to justify the indirection. The added ceremony (NuGet package, DI registration, handler discovery) is not justified for this sprint scope. MediatR would be the correct choice if the service grew to 10+ operations or if a cross-cutting pipeline (e.g., automatic cache invalidation after every command) were required.

### Alternative: Minimal API Handlers (Endpoint-per-file)
**Rejected because:** Minimal API handlers (using `app.MapGet(...)` with inline or delegate-based handlers) are appropriate for small services. However, they move business logic into the HTTP layer, making unit testing harder: testing a minimal API handler requires starting the full request pipeline rather than calling a method on a class. The requirement for production-quality unit tests with Arrange-Act-Assert patterns is better served by a typed service interface that is fully testable without HTTP infrastructure.
