using Microsoft.Extensions.Diagnostics.HealthChecks;
using PolicyManagement.Infrastructure.Data;

namespace PolicyManagement.API.HealthChecks;

/// <summary>
/// Readiness health check that verifies the SQL Server database is reachable.
/// Uses IServiceScopeFactory to correctly resolve the scoped PolicyDbContext
/// from within the singleton health check host.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connectivity check failed.", ex);
        }
    }
}
