# Security Scan Report
**Scanner:** Security Agent
**Date:** 2026-06-09
**Scope:** Full codebase — all layers (Domain, Application, Infrastructure, API, tests, Docker configuration)

---

## Findings

### [LOW] TrustServerCertificate=True in .env.example Connection String Template
- **Category:** A02 Cryptographic Failures / A05 Security Misconfiguration
- **File:** `.env.example` line 5
- **Description:** The example connection string template includes `TrustServerCertificate=True`. When developers copy this template to create their `.env`, they inherit this flag. In local development this is documented as intentional (the `PolicyDbContextFactory` comment notes "local developer SQL Server instances only"). However, if this template is copied verbatim into a staging or production `.env` without removing the flag, TLS certificate validation for the SQL Server connection is bypassed, exposing the connection to man-in-the-middle attacks.
- **Remediation:** Add a comment in `.env.example` directly on that line: `# Remove TrustServerCertificate=True for staging/production`. Additionally document in the README or deployment runbook that this flag must be removed in any non-local environment. For production, use a valid CA-signed certificate on SQL Server and omit the flag.

---

### [LOW] `docker-compose.override.yml` Is Gitignored but the Base `docker-compose.yml` Is Not — SA_PASSWORD Exposed in Healthcheck Command
- **Category:** A05 Security Misconfiguration
- **File:** `docker-compose.yml` lines 19–21
- **Description:** The SQL Server healthcheck command passes `${SA_PASSWORD}` as a CLI argument: `-P "${SA_PASSWORD}"`. This is correct in that the value is read from an environment variable (never hardcoded). However, process-level inspection tools (e.g., `docker inspect`, `ps aux`) can reveal command-line arguments, including the password value that was interpolated at container startup. This is a low-severity concern inherent to the `sqlcmd` healthcheck pattern with SQL Server, but worth noting.
- **Remediation:** Consider using a file-based credentials approach for the healthcheck (e.g., a `.sqlpasswd` file mounted as a secret) or switching to the TCP port-probe healthcheck (`/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" ...` replaced by a `curl` or `nc` TCP check on port 1433). This eliminates the password from the process argument list. If the SQL auth healthcheck is kept, ensure container-level process inspection is restricted in production via orchestration security policies.

---

### [LOW] No Upper Bound on BulkFlagRequest.PolicyIds Array Length
- **Category:** A04 Insecure Design / A05 Security Misconfiguration (Denial of Service vector)
- **File:** `src\PolicyManagement.API\Models\BulkFlagRequest.cs` line 7
- **Description:** `BulkFlagRequest.PolicyIds` is validated with `[MinLength(1)]` — a minimum of one ID is required — but there is no maximum length constraint. A caller could send thousands or millions of GUIDs in a single request. `BulkFlagAsync` translates all IDs into an `IN (...)` clause via `ids.Contains(p.Id)` (EF Core `ExecuteUpdateAsync`). Very large ID lists will generate extremely long SQL `IN` clauses, potentially exhausting memory in the deserialiser and creating expensive database queries. This is an application-layer denial-of-service risk.
- **Remediation:** Add a `[MaxLength(N)]` annotation alongside the existing `[MinLength(1)]`. Choose a limit appropriate to the business use case — for a dashboard operation flagging policies for review, a limit of 100–500 IDs is likely sufficient. Example:
  ```csharp
  [MinLength(1, ErrorMessage = "At least one policy ID is required.")]
  [MaxLength(500, ErrorMessage = "No more than 500 policy IDs may be flagged in a single request.")]
  public IReadOnlyList<Guid> PolicyIds { get; set; } = [];
  ```

---

### [LOW] Swagger UI Conditionally Enabled in Development But Not Explicitly Disabled via Route Authorization
- **Category:** A01 Broken Access Control / A05 Security Misconfiguration
- **File:** `src\PolicyManagement.API\Program.cs` lines 136–144
- **Description:** `app.UseSwagger()` and `app.UseSwaggerUI()` are guarded by `if (app.Environment.IsDevelopment())`. This is correct. However, there is no explicit `ASPNETCORE_ENVIRONMENT` enforcement in the Dockerfile or `docker-compose.yml`. If someone runs the production image without setting `ASPNETCORE_ENVIRONMENT=Production` (the default when no env var is set is `Production`, which is safe), a misconfigured deployment that sets `ASPNETCORE_ENVIRONMENT=Development` would expose the Swagger UI publicly without authentication. The risk is currently low because the default is correct, but there is no defence-in-depth.
- **Remediation:** In the Dockerfile, explicitly set `ENV ASPNETCORE_ENVIRONMENT=Production` (or do not set it at all, relying on the framework default). Add a note in the deployment runbook that `ASPNETCORE_ENVIRONMENT` must never be set to `Development` in a production or staging deployment. Optionally, add a startup assertion: `if (!app.Environment.IsDevelopment() && app.Environment.IsEnvironment("Development")) throw ...` — though the environment check already covers this.

---

### [INFO] No Authentication or Authorisation on Any Endpoint
- **Category:** A07 Authentication Failures / A01 Broken Access Control
- **File:** `src\PolicyManagement.API\Program.cs`, `src\PolicyManagement.API\Controllers\PoliciesController.cs`
- **Description:** All four API endpoints (`GET /policies`, `GET /policies/{id}`, `PATCH /policies/flag`, `GET /policies/summary`) are fully unauthenticated and unauthorised. `app.UseAuthorization()` is registered in the middleware pipeline but no `[Authorize]` attributes or authorization policies are applied to any controller or action. This is explicitly noted as out of scope in `requirements.md` ("Authentication (would be added in production)"), so this is not a defect relative to the stated requirements. It is recorded here as a mandatory pre-production remediation item.
- **Remediation:** Before any production or internet-facing deployment: add JWT Bearer authentication (Azure AD / Entra ID is the natural choice for Chubb OneHub), apply `[Authorize]` to the controller, and require appropriate role/scope claims. The `PATCH /flag` endpoint is particularly sensitive as it mutates data and should require an elevated scope.

---

### [INFO] Health Check Endpoints Are Unauthenticated and Publicly Reachable
- **Category:** A01 Broken Access Control / A05 Security Misconfiguration
- **File:** `src\PolicyManagement.API\Program.cs` lines 127–133
- **Description:** `/health/live` and `/health/ready` are mapped without any authentication requirement. The liveness endpoint returns only HTTP 200/503 with no detail. The readiness endpoint returns `"Database is reachable."` or `"Database is not reachable."` in its response body — minimal information. This pattern is standard for Kubernetes-style health probes and is generally acceptable. However, a more detailed future health check (e.g., one that reports version numbers or internal hostnames) could become an information-disclosure risk.
- **Remediation:** For the current implementation this is acceptable. If health check responses are ever expanded to include infrastructure details (connection strings, hostnames, software versions), restrict access to the health endpoints to the cluster-internal network or require a bearer token. At minimum, ensure the response body is reviewed any time the health check logic is modified.

---

### [INFO] Search Parameter Has No Maximum Length Constraint
- **Category:** A04 Insecure Design
- **File:** `src\PolicyManagement.API\Models\PolicyListRequest.cs` line 21; `src\PolicyManagement.Infrastructure\Services\PolicyService.cs` lines 62–68
- **Description:** The `Search` string parameter on `PolicyListRequest` has no `[MaxLength]` annotation. A caller could pass a search string of arbitrary length. EF Core will parameterise this as a `LIKE` pattern, so SQL injection is not a risk. However, a very long search string (e.g., 1 MB) will consume memory in the pattern construction (`$"%{query.Search}%"`) and create a large SQL parameter, potentially degrading database performance.
- **Remediation:** Add `[MaxLength(200, ErrorMessage = "Search term must not exceed 200 characters.")]` to the `Search` property. This bounds the input to a reasonable search length without affecting legitimate use.

---

### [INFO] Region Filter Has No Allow-List Validation
- **Category:** A04 Insecure Design
- **File:** `src\PolicyManagement.API\Validation\PolicyListValidationFilter.cs`; `src\PolicyManagement.Infrastructure\Services\PolicyService.cs` line 53
- **Description:** The `PolicyListValidationFilter` validates `Status`, `LineOfBusiness`, `Sort`, and `SortDirection` against allow-lists or enum parse. The `Region` parameter is not validated: any arbitrary string is accepted and passed to an EF Core `Where(p => p.Region == query.Region)` equality filter. Because this is an equality comparison (not `LIKE` or raw SQL), SQL injection is not possible — EF Core parameterises it. The risk is that callers receive a silently empty result set for any invalid region string rather than a clear 400 error. This is a usability and defensive-design concern rather than a security vulnerability.
- **Remediation:** Consider adding `Region` to the validation filter with an allow-list matching the regions defined in the seeder (`Singapore`, `Hong Kong`, `Australia`, `Japan`, `Thailand`, `Indonesia`, `Malaysia`, `Philippines`). Return a 400 with a helpful error message for unknown regions. This also prevents future confusion if a region-restricted query accidentally returns no results due to a typo.

---

## Clean Areas

**Secrets and credentials:** No hardcoded passwords, API keys, connection strings, or tokens in any source file. `appsettings.json` contains an empty string placeholder for `DefaultConnection`. `appsettings.Development.json` contains an explicit comment instructing developers to use environment variables or dotnet user-secrets. `PolicyDbContextFactory` reads exclusively from the `ConnectionStrings__DefaultConnection` environment variable and throws a clear error if absent. `docker-compose.yml` uses `${SA_PASSWORD}` and `${ConnectionStrings__DefaultConnection}` variable references throughout. `.env` is correctly listed in `.gitignore`. `.env.example` contains only placeholder values (`<your-local-sa-password>`).

**SQL Injection:** `PolicyService.ListAsync` uses exclusively EF Core LINQ (`Where`, `OrderBy`, `Skip`, `Take`, `CountAsync`, `ToListAsync`) — all translated to parameterised SQL. The `EF.Functions.Like` call constructs a `pattern` variable (`$"%{query.Search}%"`) and passes it as a parameter to EF Core — no string interpolation reaches raw SQL. The sort field allow-list (`SortFields.Allowed`) is enforced in `PolicyListValidationFilter` before the request reaches the service; additionally, `PolicyService` uses a `Dictionary<string, Expression<...>>` lookup so even if a rogue sort field reached the service, it falls back to `createdAt` rather than interpolating user input into an `ORDER BY` string. `BulkFlagAsync` uses `ids.Contains(p.Id)` which EF Core translates to a parameterised `IN` clause. `GetByIdAsync` uses `FirstOrDefaultAsync` with a typed `Guid` parameter. No raw SQL (`FromSqlRaw`, `ExecuteSqlRaw`, or ADO.NET `SqlCommand`) is used anywhere in the codebase.

**Input validation:** Page and size are bounded by `[Range(1, int.MaxValue)]` and `[Range(1, 100)]` data annotations respectively, enforced by ASP.NET Core model binding before the filter executes. `PolicyListValidationFilter` validates `Sort` against `SortFields.Allowed`, `SortDirection` against `{ "asc", "desc" }`, `Status` against `PolicyStatus` enum parse, `LineOfBusiness` against `LineOfBusiness` enum parse, and date range ordering. `BulkFlagRequest.PolicyIds` has `[MinLength(1)]`. The `{id:guid}` route constraint on `GET /policies/{id}` ensures only valid GUIDs reach the action — non-GUID path segments return 404 automatically.

**Error handling and information leakage:** `app.UseExceptionHandler()` is registered without a custom handler path, which causes ASP.NET Core's built-in Problem Details exception handler to return RFC 7807 responses without stack traces in non-Development environments. The 404 response in `GetById` returns only `"Policy with ID '{id}' was not found."` — the UUID is client-supplied, so echoing it back reveals no internal implementation detail. Serilog is configured at `Information` level for the application namespace; the logger in `GetById` logs `"Policy {PolicyId} not found"` with the GUID only — no PII (policyholder name, premium) is logged.

**Insecure configuration:** `appsettings.json` `DefaultConnection` is an empty string. `PolicyDbContextFactory` reads from environment variable and throws on missing value. Serilog suppresses `Microsoft.EntityFrameworkCore` logs at `Warning` level, preventing SQL query text from appearing in logs at default configuration.

**Dockerfile:** Multi-stage build (SDK for build, `aspnet:8.0` runtime-only image for deployment). No secrets are embedded in any `ENV`, `ARG`, or `RUN` instruction. The published output is copied from the build stage cleanly. `ASPNETCORE_URLS` is set to `http://+:8080` — HTTP only within the container, appropriate when TLS termination is handled externally (load balancer / ingress). No shell history, credential files, or development tools in the final image.

**Dependency vulnerabilities:** All packages target the .NET 8.0 ecosystem, released November 2023 and in Long-Term Support through November 2026. All floating version constraints (`8.0.*`, `8.*`) resolve to the latest patch within the major.minor band, ensuring security patches are automatically consumed on restore. Swashbuckle.AspNetCore is pinned at `10.2.1` (not a floating range) — this is a recent release with no known CVEs at the time of this scan. Serilog packages are on major versions 8.x/2.x/3.x/5.x — all current stable releases. `FluentAssertions 6.*` and `xunit 2.5.3` are test-only dependencies (never in the runtime image). `coverlet.collector 6.0.0` is a test-only development dependency. No packages with known CVEs or patterns matching historically vulnerable libraries (e.g., `Newtonsoft.Json` before 13.x, `log4net`, `ImageSharp` pre-3.x) are present.

**OWASP A08 Software Integrity Failures:** No `JsonConvert.DeserializeObject` or `BinaryFormatter` deserialization of untrusted data is present. All external input flows through typed model binding. No custom serialization of arbitrary types.

**OWASP A10 SSRF:** No outbound HTTP calls are made by this service. There are no `HttpClient`, `HttpClientFactory`, or URL-construction patterns in any service layer.

**Gitignore:** `.env`, `.env.local`, `.env.*.local`, `secrets.json`, `appsettings.local.json`, and `appsettings.*.local.json` are all explicitly gitignored. `docker-compose.override.yml` is gitignored (the committed version is intentionally an empty comment file). `.claude/settings.local.json` is gitignored.

**Clean Architecture layering:** No infrastructure concerns leak into Domain or Application layers. Domain has no external package dependencies. Application has no package dependencies. All database access is confined to Infrastructure. API depends only on Application interfaces — it never references Infrastructure types directly except for startup registration in `Program.cs` (which is an acceptable composition-root exception).

---

## Summary

| Category | Finding | Risk Level |
|---|---|---|
| A02 Cryptographic Failures | TrustServerCertificate=True in .env.example template | LOW |
| A05 Security Misconfiguration | SA_PASSWORD exposed in Docker healthcheck process args | LOW |
| A04 Insecure Design | No upper bound on BulkFlagRequest.PolicyIds array length | LOW |
| A05 Security Misconfiguration | Swagger UI not explicitly disabled in production Dockerfile | LOW |
| A07 Authentication Failures | No authentication on any endpoint (out of scope, pre-prod required) | INFO |
| A01 Broken Access Control | Health check endpoints unauthenticated (acceptable for current scope) | INFO |
| A04 Insecure Design | Search parameter has no maximum length constraint | INFO |
| A04 Insecure Design | Region filter has no allow-list validation | INFO |

**Total findings:** 8 (0 CRITICAL, 0 HIGH, 0 MEDIUM, 4 LOW, 4 INFO)

---

## Verdict

**PASS WITH NOTES**

The codebase demonstrates a strong security posture for an assessment-scope BFF service. No critical or high-severity findings were identified. The four LOW findings are hardening improvements rather than exploitable vulnerabilities in the current deployment context. The four INFO findings are either explicitly out of scope (authentication) or minor defensive-design improvements.

The single most important pre-production action is adding authentication and authorisation (INFO finding — A07/A01). All four LOW findings should be addressed before any staging or production deployment. The `TrustServerCertificate=True` template issue and the missing `PolicyIds` upper-bound are the most straightforward to fix and should be prioritised.
