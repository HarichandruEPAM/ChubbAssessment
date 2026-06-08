# Review Report — Phase 5
**Reviewer:** Reviewer Agent
**Date:** 2026-06-09
**Scope:** Phase 5 — API Layer (Controller, Validation, Error Handling, Health Checks, Logging, OpenAPI)

---

## CRITICAL findings

None.

---

## MAJOR findings

### MAJOR-01 — Sort allow-list duplicated instead of shared (ADR-009 violation)

**File:** `src\PolicyManagement.API\Validation\PolicyListValidationFilter.cs` lines 10–14; `src\PolicyManagement.Infrastructure\Services\PolicyService.cs` lines 20–31
**Standard:** ADR-009 §Decision — "The same dictionary keys are used in request validation (PolicyListQueryValidator) so the allow-list is defined once and reused."
**Issue:** ADR-009 explicitly mandates a single source of truth for the sort allow-list shared between validation and query building. The implementation uses two independent data structures: a `HashSet<string>` with 9 field names in `PolicyListValidationFilter` and a `Dictionary<string, Expression<Func<Policy, object>>>` with 9 field names in `PolicyService`. While the field names currently agree, they are textually duplicated. Any future addition or removal of a sortable field must be made in both places. Missing one creates a silent divergence: the validation layer accepts a field name that the service silently falls back to `createdAt` for, or conversely rejects a valid field the service supports. The fix is to extract the set of valid field names into a shared constant (e.g., a `public static readonly IReadOnlySet<string>` on `PolicyService` or a dedicated `SortFieldAllowList` type in the Application layer) and have the filter reference it rather than redeclare it.

### MAJOR-02 — OpenAPI spec declares `A&H` but the service serialises `AandH`

**File:** `docs\openapi.yaml` lines 44, 139 (lineOfBusiness enum values); `src\PolicyManagement.Infrastructure\Services\PolicyService.cs` lines 83, 100, 127
**Standard:** ADR-008 §Decision — "The spec is the source of truth; controllers must conform to it." Requirements §Policy Data Schema — lineOfBusiness enum `A&H`. PLAN Task 5.1 DoD — "all schema fields match the data schema in requirements."
**Issue:** The OpenAPI spec lists `A&H` as a valid `lineOfBusiness` enum value in both the query parameter (line 44) and the `Policy` schema (line 139). The C# enum member is `AandH` (required because `&` is not valid in a C# identifier). `PolicyService` serialises this value by calling `.ToString()` on the enum (lines 83, 100, 127), which produces `"AandH"`. There is no `JsonStringEnumMember` attribute, no `JsonConverter`, and no `EnumMemberAttribute` in the codebase that would map `AandH` → `"A&H"`. Consequently:
- The `GET /api/v1/policies` and `GET /api/v1/policies/{id}` endpoints return `"AandH"` in JSON, not `"A&H"` as the spec declares.
- A client filtering `?lineOfBusiness=A%26H` would pass spec validation but the `Enum.TryParse` call in the validation filter (line 37 of `PolicyListValidationFilter.cs`) would fail to parse it as `LineOfBusiness.AandH`, causing a spurious 400 unless the filter also accepts `A&H` as an alias.
- The spec and runtime behaviour diverge on every policy object returned. This is a contract violation under ADR-008.
- **Required action:** Either (a) register a `JsonStringEnumMemberConverter` (e.g., from `System.Text.Json` with a custom naming policy or `JsonStringEnumMemberAttribute`) that maps `AandH` → `"A&H"` and update the spec to match, or (b) change the spec to use `"AandH"` everywhere and document in the requirements that `A&H` is represented as `AandH` on the wire. Option (b) is simpler and avoids a custom converter but requires client acknowledgement.

---

## MINOR findings

### MINOR-01 — Swagger UI serves a Swashbuckle-generated spec, not the hand-authored `docs/openapi.yaml`

**File:** `src\PolicyManagement.API\Program.cs` lines 56–70; `docs\openapi.yaml`
**Standard:** ADR-008 §Decision — "`docs/openapi.yaml` remains the canonical document." PLAN Task 5.7 DoD — "Configure it to serve from `docs/openapi.yaml` or generate from the controller metadata."
**Issue:** `AddSwaggerGen` and `UseSwagger()` generate and serve the OpenAPI spec from controller metadata and XML comments at runtime (`/swagger/v1/swagger.json`). The hand-authored `docs/openapi.yaml` is not wired into Swashbuckle. Because the generated spec and the hand-authored spec can diverge silently (as demonstrated by MAJOR-02 above), the Swagger UI may mislead consumers about the actual contract. The DoD allows "generate from controller metadata" as an alternative, but ADR-008 states the YAML file is the canonical document. A stronger implementation would configure Swashbuckle to serve `docs/openapi.yaml` directly (e.g., via `app.UseStaticFiles()` + `options.SwaggerEndpoint("/docs/openapi.yaml", ...)`) or add a CI step comparing the generated spec against the committed YAML to detect drift. As-is, the risk of the two specs drifting is real and not mitigated.

### MINOR-02 — `BulkFlagRequest.PolicyIds` lacks a data-annotation `[Required]` / `[MinLength(1)]` guard

**File:** `src\PolicyManagement.API\Models\BulkFlagRequest.cs` lines 1–6; `src\PolicyManagement.API\Controllers\PoliciesController.cs` lines 49–53
**Standard:** CLAUDE.md §Code Quality — "Validate and sanitize all external inputs." PLAN Task 5.3 — "Validate all inbound request parameters. Return 400 with a structured Problem Details body on validation failure."
**Issue:** `BulkFlagRequest` has no data annotations on `PolicyIds`. The null/empty check is performed manually inside the action method (lines 49–53). This is inconsistent with the annotation-driven approach used by `PolicyListRequest` (which uses `[Range]` attributes). The manual check works correctly, but it bypasses the `InvalidModelStateResponseFactory` configured in `Program.cs` and instead calls `ValidationProblem()` directly. The resulting Problem Details body is structurally equivalent, but the pattern inconsistency means future contributors may not realise they need to add manual validation for models that lack annotations. Adding `[Required]` and a custom `[MinLength(1)]`-equivalent annotation to `PolicyIds` would make the validation surface uniform and delegate to the shared factory.

### MINOR-03 — `openapi.yaml` sort parameter `enum` list includes `updatedAt` but the Plan and ADR-009 do not list it

**File:** `docs\openapi.yaml` line 28; `.docs\adr\009-sort-field-allow-list.md` §Decision (code block)
**Standard:** ADR-009 §Decision — defines the canonical allow-list with 9 entries. The listed entries in the ADR code example do not include `updatedAt`; the ADR lists only `createdAt` (not `updatedAt`).
**Issue:** ADR-009's illustrative code block (the `SortSelectors` dictionary) lists 9 keys, ending with `["createdat"]`, and does not include `updatedAt`. The actual implementation in `PolicyService.cs` and `PolicyListValidationFilter.cs` both include `updatedAt` as a valid sort field. The spec also includes it. The implementation is internally consistent, but it diverges from the ADR example. This is a documentation drift: the ADR should be updated to list `updatedAt` explicitly so it remains the authoritative source for the allow-list. This is low-risk since the three code artefacts agree, but the ADR's stated allow-list is incomplete.

### MINOR-04 — `appsettings.json` contains an empty `DefaultConnection` key, which may mislead developers

**File:** `src\PolicyManagement.API\appsettings.json` lines 9–11
**Standard:** CLAUDE.md §Security — "Never hardcode secrets, passwords, API keys, or connection strings in code." PLAN Task 1.4 — "secrets are not present in any committed configuration file."
**Issue:** The committed `appsettings.json` contains `"DefaultConnection": ""`. While the value is empty (not a real credential), the key's presence may confuse developers who try setting the connection string in this file instead of via the documented environment variable `ConnectionStrings__DefaultConnection`. A clearer approach is to omit the key entirely or add an inline comment block explaining that this value must come from the environment variable (though JSON does not support comments). At minimum, a code comment in `Program.cs` or the README should make the precedence obvious. The current file partially contradicts Task 1.4's DoD, which requires that "secrets are not present in any committed configuration file." An empty string is not a secret, but the key implies the file is the intended configuration location.

---

## OBSERVATIONS

### OBS-01 — `UseSerilogRequestLogging()` is placed after `UseExceptionHandler()` — request-logging position is correct but worth confirming intent

**File:** `src\PolicyManagement.API\Program.cs` lines 111, 114
**Standard:** PLAN Task 5.6 — "Log: incoming request method and path, handler entry and exit with duration."
**Detail:** `UseExceptionHandler()` is at line 111 and `UseSerilogRequestLogging()` is at line 114. Placing Serilog request logging after the exception handler is the recommended order: it ensures that exceptions caught by the handler are already resolved before Serilog logs the response status code, which avoids double-logging the failure. This is correct. Noting for completeness.

### OBS-02 — `UseSwaggerUI` is registered after `MapControllers()` in the middleware pipeline

**File:** `src\PolicyManagement.API\Program.cs` lines 120, 133–141
**Standard:** Conventional ASP.NET Core middleware ordering.
**Detail:** `MapControllers()` is called at line 120. The `UseSwagger()` / `UseSwaggerUI()` calls are at lines 135–141. In ASP.NET Core, `UseSwagger` and `UseSwaggerUI` are terminal middleware and do not need to precede `MapControllers`. However, conventional scaffolding places them before `app.Run()` regardless of order relative to `MapControllers`. This is functionally correct in .NET 8 but is worth noting for consistency with standard templates.

### OBS-03 — No `PagedResult` / `totalPages` field in `PaginatedPolicies` schema

**File:** `docs\openapi.yaml` lines 152–162
**Standard:** Requirements §API Endpoints — "list with pagination." PLAN Task 5.1 — "PaginatedResult schema."
**Detail:** The `PaginatedPolicies` schema exposes `totalCount`, `page`, and `size` but not a computed `totalPages` field. Clients must compute `ceil(totalCount / size)` themselves to render paging controls. This is a design choice, not a bug, but it is less ergonomic than including `totalPages`. No requirement mandates it, and the `PaginatedResult<T>` DTO in the Application layer may also omit it. This observation is for the architect's awareness in case a frontend is introduced.

### OBS-04 — `GetSummaryAsync` performs three sequential database round-trips

**File:** `src\PolicyManagement.Infrastructure\Services\PolicyService.cs` lines 117–136
**Standard:** CLAUDE.md §Async and Data Access — "Use IQueryable composition for database filtering; defer execution."
**Detail:** `GetSummaryAsync` issues three separate `await` calls (statusCounts, premiumByLob, expiringSoon) in sequence. The plan's Task 4.3 DoD says "`ListAsync` produces a single SQL query" but does not mandate a single round-trip for `GetSummaryAsync`. Three queries for a summary endpoint is reasonable given the differing aggregation shapes, but the method could also benefit from `Task.WhenAll` if the operations are independent and the `DbContext` lifetime allows concurrent usage (it does not by default with EF Core — DbContext is not thread-safe). This is noted as an architectural observation rather than a defect.

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| MAJOR | 2 |
| MINOR | 4 |
| OBSERVATION | 4 |

## Verdict

REQUEST CHANGES

## Recommendation

Phase 5 is structurally sound: route ordering is correct (RISK-07 resolved), the health check implementation is exemplary, Problem Details wiring is complete and consistent, Serilog is correctly configured with JSON output and environment enrichment, Swagger is properly gated to Development, and controller actions are clean with no business logic or oversized methods. Two issues require resolution before this phase can be signed off. MAJOR-02 (the `A&H` vs `AandH` contract mismatch) is the more urgent of the two: every policy object returned by the API carries a `lineOfBusiness` value that contradicts the OpenAPI spec, which violates the contract-first principle of ADR-008. MAJOR-01 (allow-list duplication) violates ADR-009 directly and is a maintainability time-bomb — the two lists agree today but will silently diverge the moment a new sort field is added. Both majors have straightforward fixes (a shared constant type for the allow-list; a `JsonStringEnumMemberAttribute` or spec correction for the enum wire value), and neither requires architectural rework. Once those two issues are addressed and the minor inconsistency in `BulkFlagRequest` validation is aligned with the annotation-driven pattern used elsewhere, this phase meets the Definition of Done.
