# ADR-005: Enums Stored as Strings via HasConversion<string>()

**Status:** Accepted
**Date:** 2026-06-08

## Context

EF Core stores C# enums as integers by default (e.g., `PolicyStatus.Active` → `0`, `PolicyStatus.Expired` → `1`). The API contract uses string representations throughout: the OpenAPI specification defines enum values as `"Active"`, `"Expired"`, `"Property"`, `"Marine"` etc. SQL Server stores and displays the numeric value, which is opaque to anyone querying the database directly.

The requirements state the API is contract-first with string enum values. Storing integers in the database creates a mapping layer between the stored value and the API value, and a debugging hazard where a direct SQL query returns `0` where a developer expects `"Active"`.

RISK-06 in the plan identifies this as a deliberate decision point. The plan's recommended resolution is to store all enums as strings.

## Decision

All enum columns (`Status`, `LineOfBusiness`) are configured in `PolicyConfiguration.cs` using `.HasConversion<string>()` in the EF Core fluent API. The stored value in SQL Server is the enum member name as a string (e.g., `N'Active'`, `N'Property'`).

```csharp
builder.Property(p => p.Status)
    .HasConversion<string>()
    .HasMaxLength(20);

builder.Property(p => p.LineOfBusiness)
    .HasConversion<string>()
    .HasMaxLength(20);
```

## Consequences

### Positive
- The database value matches the API contract value exactly; no translation layer is needed when reading or writing enum columns.
- Direct SQL queries (for debugging, reporting, or data migration) return human-readable strings without a lookup table or mental mapping.
- Filtering by enum value in EF Core LINQ queries (e.g., `.Where(p => p.Status == PolicyStatus.Active)`) produces the correct SQL predicate against the string column.
- Aligns with the OpenAPI specification, which defines all enum values as strings, making the full stack (API response → EF Core → SQL Server column) consistent.

### Negative / Trade-offs
- Renaming an enum member (e.g., `AandH` → `AccidentAndHealth`) is a breaking migration change: a new migration must `UPDATE` existing rows to the new string value before the column can be altered. This must be documented and enforced via a migration script.
- String columns consume slightly more storage than integer columns (20 bytes vs 4 bytes per row). At 200 seed records and small production volumes this is not a practical concern.
- The string conversion must be configured explicitly for every enum property; forgetting the configuration silently reverts to integer storage. This is mitigated by the `PolicyConfiguration` unit test that verifies column type.

## Alternatives Considered

### Alternative: Store as int (EF Core Default)
**Rejected because:** The integer representation has no correspondence to the API contract or any human-readable value. Every direct database query requires a lookup table or developer knowledge of the enum ordinal mapping. When enum members are reordered in code, the ordinal values shift silently, corrupting existing data without a migration. These hazards outweigh the marginal storage benefit.

### Alternative: Separate Lookup Table (e.g., PolicyStatuses table)
**Rejected because:** A lookup table introduces foreign key constraints, additional joins on every query, and additional migrations whenever an enum value is added. The enum values are defined by the business domain (requirements §Policy Data Schema) and are stable over the assessment period. A lookup table would add infrastructure complexity with no benefit for a read-heavy BFF service with a fixed set of known values.
