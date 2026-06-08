# ADR-004: SQL Server 2022 as the Database Engine

**Status:** Accepted
**Date:** 2026-06-08

## Context

The service requires a relational database to store and query policy records. The requirements document explicitly states the technology target as "SQL Server 2022 (matches Chubb OneHub production on Azure SQL)". The production deployment runs on Azure SQL, which is a managed SQL Server service. The assessment environment uses Docker Compose locally, so the database must be available as a container image. `CLAUDE.md` states: "Prefer the choice that matches the stated production environment."

EF Core's SQL Server provider (`Microsoft.EntityFrameworkCore.SqlServer`) is the most mature and well-tested of the EF Core providers, with full support for SQL Server-specific features, reliable translation of `IQueryable` expressions, and first-class Azure SQL compatibility.

## Decision

SQL Server 2022 is used as the database engine. The Docker Compose environment uses the `mcr.microsoft.com/mssql/server:2022-latest` container image. EF Core is configured with the `UseSqlServer()` provider. The connection string is supplied via the `ConnectionStrings__DefaultConnection` environment variable.

## Consequences

### Positive
- Exact match between local development and production (Azure SQL); SQL dialect, type system, and collation behaviour are identical, eliminating "works locally, fails in production" translation issues.
- The EF Core SQL Server provider handles SQL Server-specific expressions (e.g., `LIKE`, `ExecuteUpdateAsync` translation, decimal precision) without workarounds.
- No risk of SQL translation divergence: a query that works in development against SQL Server is the same query that runs against Azure SQL in production.
- SQL Server 2022 supports JSON functions, full-text search, and temporal tables — all useful for future features listed in the "What I Would Do Next" section.

### Negative / Trade-offs
- The SQL Server container image is large (~1.5 GB pulled), making `docker-compose up` slower on first run compared to PostgreSQL or SQLite.
- SQL Server requires a license for production use (Azure SQL is billed per DTU/vCore). For a local developer environment this is free via the Developer Edition embedded in the container.
- Developers on hardware with less than 2 GB of free RAM may find the SQL Server container resource-intensive compared to PostgreSQL or SQLite.

## Alternatives Considered

### Alternative: PostgreSQL
**Rejected because:** PostgreSQL does not match the Chubb OneHub production stack. While the EF Core Npgsql provider is capable and open-source, using PostgreSQL locally creates a risk of SQL translation differences at runtime (e.g., case sensitivity in `LIKE`, identifier quoting conventions, decimal arithmetic edge cases). `CLAUDE.md` and the requirements both explicitly state SQL Server 2022 as the target.

### Alternative: SQLite (in-memory or file-based)
**Rejected because:** SQLite is appropriate for local development shortcuts or unit tests but is not suitable as the primary database for an assessment that explicitly targets SQL Server 2022. SQLite does not enforce foreign keys by default, has limited type affinity, and does not support `ExecuteUpdateAsync` in the same way. Using SQLite as the runtime database would misrepresent production behaviour and contradict the stated technology target.
