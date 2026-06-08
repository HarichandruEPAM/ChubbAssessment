# ADR-010: TimeProvider Injection for Expiring-Soon Calculation

**Status:** Accepted
**Date:** 2026-06-08

## Context

The `GetSummaryAsync` method must compute an "expiring-soon" count: the number of active policies whose `ExpiryDate` falls within a configurable threshold (default 30 days) of the current date. This calculation requires access to "now." The naive implementation calls `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly inside the service method.

Calling `DateTime.UtcNow` directly in service code has a critical testability problem: the result changes with wall-clock time, making any test that asserts the expiring-soon count depend on the current date at test execution. A test seeded with policies expiring "in 10 days" will pass today and fail in 11 days without any code change.

.NET 8 introduces `System.TimeProvider` as an abstract base class for abstracting the system clock. It is part of the .NET 8 BCL with no additional NuGet dependency.

## Decision

`PolicyService` receives a `TimeProvider` instance via constructor injection. The current UTC time is obtained as `_timeProvider.GetUtcNow().UtcDateTime` wherever the current date is needed.

```csharp
public PolicyService(PolicyDbContext context, TimeProvider timeProvider, ...)
{
    _context = context;
    _timeProvider = timeProvider;
}
```

In `Program.cs` (or `DependencyInjection.cs`), the system `TimeProvider` is registered:

```csharp
services.AddSingleton(TimeProvider.System);
```

In unit tests, a `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing` or a manual subclass) is constructed with a fixed date and injected:

```csharp
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 06, 08, 0, 0, 0, TimeSpan.Zero));
var service = new PolicyService(context, fakeTime, ...);
```

This makes the expiring-soon calculation completely deterministic regardless of when the test runs.

## Consequences

### Positive
- Tests that assert the expiring-soon count are deterministic: the test controls "now" via the injected `TimeProvider` and the result is reproducible on any date, in any timezone, on any CI agent.
- `TimeProvider` is a .NET 8 BCL type; no additional NuGet package is required for production code.
- The injection pattern is consistent with ASP.NET Core's built-in DI model; `TimeProvider.System` is a singleton with no lifecycle concerns.
- `BulkFlagAsync` can also use `_timeProvider.GetUtcNow()` for the `UpdatedAt` timestamp, making that operation similarly deterministic in tests.

### Negative / Trade-offs
- `TimeProvider` is an abstract class, not an interface. Subclassing it for test doubles requires either `Microsoft.Extensions.TimeProvider.Testing` (a NuGet package) or a minimal manual subclass. The manual subclass approach adds a few lines of test helper code but requires zero additional production dependencies.
- Developers unfamiliar with `TimeProvider` may instinctively write `DateTime.UtcNow` when extending the service. This should be documented in `CLAUDE.md` or a code review checklist.

## Alternatives Considered

### Alternative: DateTime.UtcNow Directly in Service Code
**Rejected because:** `DateTime.UtcNow` is a static call that cannot be injected, mocked, or controlled in tests. Any test for `GetSummaryAsync` that involves expiring-soon counts must be written relative to the real current date, which means test data seeded with "expiry in 15 days" will stop passing after those 15 days pass. This makes the test suite time-sensitive and eventually broken without any developer error. The requirement for independent, isolated tests (CLAUDE.md §Testing) makes this approach unacceptable.

### Alternative: Custom IClock Interface
**Rejected because:** Defining a project-specific `IClock` interface (e.g., `interface IClock { DateTimeOffset UtcNow { get; } }`) was the standard approach before .NET 8. It achieves the same testability goal as `TimeProvider` but is now redundant: `TimeProvider` is the official .NET abstraction for the system clock, ships in the BCL, and is recognised by the broader .NET ecosystem (including `FakeTimeProvider` in the testing package). Creating a custom `IClock` duplicates an already-provided abstraction and introduces a proprietary type that new developers must learn instead of reaching for the documented .NET standard.
