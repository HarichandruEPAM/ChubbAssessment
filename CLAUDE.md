# Engineering Constraints (Global)

## Purpose
These constraints apply to all work in this repository. They are
project-agnostic engineering standards. Task-specific requirements
live separately in .specs/requirements.md.

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
