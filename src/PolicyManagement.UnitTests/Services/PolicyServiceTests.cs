using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Infrastructure.Data;
using PolicyManagement.Infrastructure.Services;
using Xunit;

namespace PolicyManagement.UnitTests.Services;

public class PolicyServiceTests
{
    // ---------------------------------------------------------------------------
    // Helper factory
    // ---------------------------------------------------------------------------

    private static (PolicyDbContext db, PolicyService service) CreateService(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<PolicyDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        var db = new PolicyDbContext(options, TimeProvider.System);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicySummary:ExpiringSoonThresholdDays"] = "30" })
            .Build();
        var service = new PolicyService(db, TimeProvider.System, config);
        return (db, service);
    }

    /// <summary>
    /// Creates a SQLite in-memory service that supports ExecuteUpdateAsync.
    /// EF Core InMemory does not support bulk update operations (ExecuteUpdateAsync);
    /// SQLite in-memory is used for BulkFlagAsync tests as it is a relational provider.
    /// The caller must dispose the returned SqliteConnection when done.
    /// </summary>
    private static (SqliteConnection connection, PolicyDbContext db, PolicyService service) CreateSqliteService()
    {
        // Keep the connection open for the duration of the test — SQLite in-memory DB
        // is scoped to a single open connection.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<PolicyDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new PolicyDbContext(options, TimeProvider.System);
        db.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicySummary:ExpiringSoonThresholdDays"] = "30" })
            .Build();

        var service = new PolicyService(db, TimeProvider.System, config);
        return (connection, db, service);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private static Policy MakePolicy(string policyNumber, PolicyStatus status = PolicyStatus.Active,
        DateTime? createdAt = null, DateTime? expiryDate = null)
    {
        var now = DateTime.UtcNow;
        return new Policy
        {
            Id = Guid.NewGuid(),
            PolicyNumber = policyNumber,
            PolicyholderName = $"Holder {policyNumber}",
            LineOfBusiness = LineOfBusiness.Property,
            Status = status,
            PremiumAmount = 10_000m,
            Currency = "USD",
            EffectiveDate = now.AddDays(-30),
            ExpiryDate = expiryDate ?? now.AddDays(365),
            Region = "APAC",
            Underwriter = "Underwriter A",
            FlaggedForReview = false,
            CreatedAt = createdAt ?? now,
            UpdatedAt = now
        };
    }

    // ===========================================================================
    // ListAsync tests
    // ===========================================================================

    [Fact]
    public async Task ListAsync_NoPagination_ReturnsAllPolicies()
    {
        // Arrange
        var (db, service) = CreateService();
        db.Policies.AddRange(
            MakePolicy("POL-000001"),
            MakePolicy("POL-000002"),
            MakePolicy("POL-000003"));
        await db.SaveChangesAsync();

        var query = new PolicyListQuery(Page: 1, Size: 10);

        // Act
        var result = await service.ListAsync(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(3);
        result.Items.Count.Should().Be(3);
    }

    [Fact]
    public async Task ListAsync_Pagination_ReturnsCorrectPage()
    {
        // Arrange
        var (db, service) = CreateService();
        db.Policies.AddRange(
            MakePolicy("POL-000001"),
            MakePolicy("POL-000002"),
            MakePolicy("POL-000003"),
            MakePolicy("POL-000004"),
            MakePolicy("POL-000005"));
        await db.SaveChangesAsync();

        var query = new PolicyListQuery(Page: 2, Size: 2, Sort: "policyNumber", SortDirection: "asc");

        // Act
        var result = await service.ListAsync(query, CancellationToken.None);

        // Assert
        result.Items.Count.Should().Be(2);
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_StatusFilter_ReturnsMatchingOnly()
    {
        // Arrange
        var (db, service) = CreateService();
        db.Policies.AddRange(
            MakePolicy("POL-000001", PolicyStatus.Active),
            MakePolicy("POL-000002", PolicyStatus.Active),
            MakePolicy("POL-000003", PolicyStatus.Expired),
            MakePolicy("POL-000004", PolicyStatus.Expired));
        await db.SaveChangesAsync();

        var query = new PolicyListQuery(Page: 1, Size: 10, Status: "Active");

        // Act
        var result = await service.ListAsync(query, CancellationToken.None);

        // Assert
        result.Items.Should().AllSatisfy(item => item.Status.Should().Be("Active"));
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_SearchByPolicyNumber_ReturnsMatch()
    {
        // Arrange
        var (db, service) = CreateService();
        db.Policies.AddRange(
            MakePolicy("POL-ALPHA1"),
            MakePolicy("POL-BETA22"),
            MakePolicy("POL-GAMMA3"));
        await db.SaveChangesAsync();

        var query = new PolicyListQuery(Page: 1, Size: 10, Search: "BETA");

        // Act
        var result = await service.ListAsync(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.PolicyNumber == "POL-BETA22");
    }

    [Fact]
    public async Task ListAsync_UnknownSortField_FallsBackToCreatedAt()
    {
        // Arrange
        var (db, service) = CreateService();
        var older = MakePolicy("POL-000001", createdAt: DateTime.UtcNow.AddDays(-10));
        var newer = MakePolicy("POL-000002", createdAt: DateTime.UtcNow.AddDays(-1));
        db.Policies.AddRange(older, newer);
        await db.SaveChangesAsync();

        var query = new PolicyListQuery(Page: 1, Size: 10, Sort: "unknownField", SortDirection: "desc");

        // Act
        var result = await service.ListAsync(query, CancellationToken.None);

        // Assert
        result.Items.Should().NotBeEmpty();
        result.TotalCount.Should().Be(2);
        // Falls back to createdAt desc — newest first
        result.Items[0].PolicyNumber.Should().Be("POL-000002");
        result.Items[1].PolicyNumber.Should().Be("POL-000001");
    }

    // ===========================================================================
    // GetByIdAsync tests
    // ===========================================================================

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsPolicyDetail()
    {
        // Arrange
        var (db, service) = CreateService();
        var policy = MakePolicy("POL-EXIST1");
        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        // Act
        var result = await service.GetByIdAsync(policy.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(policy.Id);
        result.PolicyNumber.Should().Be("POL-EXIST1");
        result.PolicyholderName.Should().Be(policy.PolicyholderName);
        result.Status.Should().Be("Active");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        // Arrange
        var (_, service) = CreateService();

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ===========================================================================
    // BulkFlagAsync tests
    // ===========================================================================

    [Fact]
    public async Task BulkFlagAsync_ValidIds_FlagsAllAndReturnsCount()
    {
        // Arrange — SQLite in-memory required: EF Core InMemory does not support ExecuteUpdateAsync
        var (connection, db, service) = CreateSqliteService();
        await using var _ = connection;
        await using var __ = db;

        var p1 = MakePolicy("POL-FLAG01");
        var p2 = MakePolicy("POL-FLAG02");
        var p3 = MakePolicy("POL-FLAG03");
        db.Policies.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        // Act
        var result = await service.BulkFlagAsync(new[] { p1.Id, p2.Id }, CancellationToken.None);

        // Assert
        result.Flagged.Should().Be(2);
        result.NotFound.Should().Be(0);

        db.ChangeTracker.Clear();
        var flaggedPolicies = await db.Policies
            .Where(p => p.Id == p1.Id || p.Id == p2.Id)
            .ToListAsync();
        flaggedPolicies.Should().AllSatisfy(p => p.FlaggedForReview.Should().BeTrue());
    }

    [Fact]
    public async Task BulkFlagAsync_Idempotent_AlreadyFlaggedPolicies()
    {
        // Arrange — SQLite in-memory required: EF Core InMemory does not support ExecuteUpdateAsync
        var (connection, db, service) = CreateSqliteService();
        await using var _ = connection;
        await using var __ = db;

        var policy = MakePolicy("POL-IDEM01");
        policy.FlaggedForReview = true;
        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        // Act
        var result = await service.BulkFlagAsync(new[] { policy.Id }, CancellationToken.None);

        // Assert
        // ExecuteUpdateAsync counts rows matched by WHERE — idempotent: already-flagged row still matches
        result.Flagged.Should().Be(1);
        result.NotFound.Should().Be(0);
    }

    [Fact]
    public async Task BulkFlagAsync_PartialIds_ReturnsCorrectCounts()
    {
        // Arrange — SQLite in-memory required: EF Core InMemory does not support ExecuteUpdateAsync
        var (connection, db, service) = CreateSqliteService();
        await using var _ = connection;
        await using var __ = db;

        var p1 = MakePolicy("POL-PART01");
        var p2 = MakePolicy("POL-PART02");
        db.Policies.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var unknownId = Guid.NewGuid();

        // Act
        var result = await service.BulkFlagAsync(new[] { p1.Id, p2.Id, unknownId }, CancellationToken.None);

        // Assert
        result.Flagged.Should().Be(2);
        result.NotFound.Should().Be(1);
    }

    // ===========================================================================
    // GetSummaryAsync tests
    // ===========================================================================

    [Fact]
    public async Task GetSummaryAsync_StatusCounts_ReturnsCorrectCounts()
    {
        // Arrange
        var (db, service) = CreateService();
        db.Policies.AddRange(
            MakePolicy("POL-SUM001", PolicyStatus.Active),
            MakePolicy("POL-SUM002", PolicyStatus.Active),
            MakePolicy("POL-SUM003", PolicyStatus.Expired),
            MakePolicy("POL-SUM004", PolicyStatus.Pending));
        await db.SaveChangesAsync();

        // Act
        var result = await service.GetSummaryAsync(CancellationToken.None);

        // Assert
        result.StatusCounts["Active"].Should().Be(2);
        result.StatusCounts["Expired"].Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_ExpiringSoon_UsesThresholdFromConfig()
    {
        // Arrange
        var fakeNow = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(fakeNow);

        var options = new DbContextOptionsBuilder<PolicyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new PolicyDbContext(options, fakeTimeProvider);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicySummary:ExpiringSoonThresholdDays"] = "30" })
            .Build();
        var service = new PolicyService(db, fakeTimeProvider, config);

        var nowUtc = fakeNow.UtcDateTime;

        // Policy expiring in 15 days — within the 30-day threshold
        var expiringSoon = new Policy
        {
            Id = Guid.NewGuid(),
            PolicyNumber = "POL-SOON01",
            PolicyholderName = "Holder A",
            LineOfBusiness = LineOfBusiness.Property,
            Status = PolicyStatus.Active,
            PremiumAmount = 5_000m,
            Currency = "USD",
            EffectiveDate = nowUtc.AddDays(-30),
            ExpiryDate = nowUtc.AddDays(15),
            Region = "APAC",
            Underwriter = "Underwriter A",
            FlaggedForReview = false,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };

        // Policy expiring in 45 days — outside the 30-day threshold
        var notExpiringSoon = new Policy
        {
            Id = Guid.NewGuid(),
            PolicyNumber = "POL-FAR001",
            PolicyholderName = "Holder B",
            LineOfBusiness = LineOfBusiness.Casualty,
            Status = PolicyStatus.Active,
            PremiumAmount = 8_000m,
            Currency = "USD",
            EffectiveDate = nowUtc.AddDays(-30),
            ExpiryDate = nowUtc.AddDays(45),
            Region = "APAC",
            Underwriter = "Underwriter B",
            FlaggedForReview = false,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };

        db.Policies.AddRange(expiringSoon, notExpiringSoon);
        await db.SaveChangesAsync();

        // Act
        var result = await service.GetSummaryAsync(CancellationToken.None);

        // Assert
        result.ExpiringSoonCount.Should().Be(1);
    }
}
