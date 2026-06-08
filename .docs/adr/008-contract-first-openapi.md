# ADR-008: Contract-First OpenAPI (Write openapi.yaml Before Controllers)

**Status:** Accepted
**Date:** 2026-06-08

## Context

The requirements explicitly state "OpenAPI 3.x, contract-first" as a technology requirement. There are two broad approaches to producing an OpenAPI specification for an ASP.NET Core API:

1. **Contract-first:** Write the `openapi.yaml` file by hand before writing any controller code. The spec is the authoritative source of truth. Controllers are written to conform to the spec.
2. **Code-first:** Write controller code with attributes and XML comments. A tool (Swashbuckle, NSwag, or .NET 9 built-in OpenAPI) generates the spec from the code at runtime or build time. The code is the authoritative source.

The development plan (Task 5.1) mandates the contract-first approach and commits the spec at `docs/openapi.yaml` before controller development begins.

## Decision

`docs/openapi.yaml` is written as a complete OpenAPI 3.x specification before any controller code is written. The spec defines all four endpoints, all query parameters with types and defaults, all request/response schemas (including enum string values, pagination structure, and Problem Details error responses), and standard HTTP status codes (200, 204, 400, 404, 500).

Controllers are written to match the spec exactly. Deviations from the spec in controller output are treated as bugs. Swashbuckle is configured in Development mode to serve the spec for interactive exploration, but `docs/openapi.yaml` remains the canonical document.

## Consequences

### Positive
- The API surface is fully designed and reviewable before a single line of implementation code is written. Design issues (ambiguous parameters, missing fields, route conflicts) are caught at specification time, not during integration testing.
- The spec can be validated with a linter (e.g., Spectral) independently of the implementation, providing early feedback on API design quality.
- Client teams (frontend, other BFFs) can begin client-side code generation (`openapi-generator`, `NSwag`) from the spec before the server implementation is complete.
- Explicit schema definitions in the spec force deliberate decisions about field types, nullability, and enum values — decisions that code-first generation often makes implicitly and inconsistently.
- RISK-07 (the `/flag` vs `/{id:guid}` route conflict) is visible and resolvable at spec design time, before routing is tested manually.

### Negative / Trade-offs
- The spec and the controller implementation are two separate artefacts that can drift out of sync. If a controller is changed without updating `openapi.yaml`, the spec becomes stale. This requires a discipline of updating the spec as part of any API change, or a CI validation step that compares generated spec against `openapi.yaml`.
- Writing YAML by hand is more verbose and error-prone than generating from code. Schema references (`$ref`) and YAML indentation errors are common mistakes.
- For teams unfamiliar with OpenAPI 3.x syntax, contract-first has a higher initial learning curve than code-first generation.

## Alternatives Considered

### Alternative: Code-First (Generate Spec from Attributes)
**Rejected because:** Code-first generation (via Swashbuckle or .NET 9 built-in OpenAPI) produces a spec that reflects the code as-written, which may not reflect the intended API design. Attributes and XML comments on controllers are often incomplete, and the generated spec is frequently inaccurate for complex scenarios (e.g., discriminated unions, polymorphic responses, custom error shapes). More importantly, code-first makes the spec a derivative artefact rather than the source of truth, which contradicts the explicit "contract-first" requirement.

### Alternative: No Formal Spec
**Rejected because:** The requirements explicitly list "OpenAPI specification file" as a deliverable. A working API without a formal spec cannot be used for client-side code generation, automated testing against the spec, or onboarding new consumers. The plan's project-level Definition of Done (item 6) requires `docs/openapi.yaml` to exist and pass OpenAPI 3.x linting.
