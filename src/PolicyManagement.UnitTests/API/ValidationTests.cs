using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManagement.Infrastructure.Data;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PolicyManagement.UnitTests.API;

public class ValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ValidationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");

            builder.ConfigureServices(services =>
            {
                // Remove the real SQL Server DbContext registration
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<PolicyDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                // Replace with InMemory — use a unique name per test instance so tests
                // are fully isolated (CLAUDE.md: "no shared mutable state").
                services.AddDbContext<PolicyDbContext>((sp, options) =>
                    options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
                    ServiceLifetime.Scoped);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task ListPolicies_PageZero_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies?page=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("problem+json");
    }

    [Fact]
    public async Task ListPolicies_SizeZero_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies?size=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListPolicies_SizeOverMax_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies?size=101");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListPolicies_InvalidStatus_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies?status=InvalidValue");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListPolicies_DateRangeInverted_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies?effectiveDateFrom=2025-01-01&effectiveDateTo=2024-01-01");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkFlag_EmptyList_Returns400()
    {
        // Arrange
        var body = new { policyIds = Array.Empty<Guid>() };

        // Act
        var response = await _client.PatchAsJsonAsync("/api/v1/policies/flag", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_NonExistentGuid_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies/00000000-0000-0000-0000-000000000001");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("problem+json");
    }
}
