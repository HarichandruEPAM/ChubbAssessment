# Requirements — Policy Management BFF Service

## Context
Chubb APAC operations manage insurance policies across multiple regions,
currently using spreadsheets. This service is a Backend-for-Frontend (BFF)
that aggregates, transforms, and serves policy data to a dashboard.

## Technology Target
- .NET 8 (C#)
- SQL Server 2022 (matches Chubb OneHub production on Azure SQL)
- Entity Framework Core with migrations
- xUnit for testing
- Docker Compose for local setup
- OpenAPI 3.x, contract-first

## API Endpoints (contract-first)
1. GET /api/v1/policies — list with pagination, sorting, filtering, free-text search
2. GET /api/v1/policies/{id} — single policy by ID
3. PATCH /api/v1/policies/flag — bulk flag policies for review (array of IDs)
4. GET /api/v1/policies/summary — counts by status, total premium by line of business, expiring-soon count

## List Endpoint Parameters
- page, size (sensible defaults)
- sort (field and direction, e.g. premiumAmount,desc)
- status filter (Active, Expired, Pending, Cancelled)
- lineOfBusiness filter (Property, Casualty, A&H, Marine)
- region filter
- effectiveDateFrom / effectiveDateTo range
- search across policyNumber, policyholderName, underwriter

## Policy Data Schema
| Field | Type | Notes |
|---|---|---|
| id | UUID | Primary key |
| policyNumber | String | Unique, format POL-XXXXXX |
| policyholderName | String | Realistic APAC names |
| lineOfBusiness | Enum | Property, Casualty, A&H, Marine |
| status | Enum | Active, Expired, Pending, Cancelled |
| premiumAmount | Decimal | 1,000 – 5,000,000 |
| currency | String | USD, SGD, HKD, AUD, JPY, THB |
| effectiveDate | Date | |
| expiryDate | Date | |
| region | String | Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines |
| underwriter | String | |
| flaggedForReview | Boolean | Default false |
| createdAt | Timestamp | |
| updatedAt | Timestamp | |

## Database
- Relational database, schema managed via migrations
- Seed 200+ realistic policy records covering all statuses, lines of
  business, regions, and a realistic spread of dates and premiums

## Required Engineering Standards
- Clean Architecture with clear layering
- Production-quality test automation
- Cross-cutting concerns: logging, error handling, health checks,
  externalized configuration, API documentation, runnable local setup

## Bonus (only if time permits)
- Caching for summary statistics with invalidation strategy
- Kafka producer for flag events, consumer for status changes, idempotent

## Deliverables
- Git repository with meaningful commit history
- Working service, ideally via docker-compose up
- OpenAPI specification file
- AI working journal (accepted / challenged / overridden, with reasoning)
- Supporting documentation (architecture decisions, trade-offs)

## Out of Scope (deliberate)
- Frontend dashboard (this is the backend-only track)
- Authentication (would be added in production)
