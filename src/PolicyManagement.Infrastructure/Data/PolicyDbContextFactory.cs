using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PolicyManagement.Infrastructure.Data;

// Design-time only — used by 'dotnet ef' tooling (migrations add/update/script).
// Never instantiated at runtime. TrustServerCertificate=True is acceptable here
// because this factory targets local developer SQL Server instances only.
public class PolicyDbContextFactory : IDesignTimeDbContextFactory<PolicyDbContext>
{
    public PolicyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection environment variable is not set. " +
                "Set it before running 'dotnet ef' commands.");

        var options = new DbContextOptionsBuilder<PolicyDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new PolicyDbContext(options, TimeProvider.System);
    }
}
