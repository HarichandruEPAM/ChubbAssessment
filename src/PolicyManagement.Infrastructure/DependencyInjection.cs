using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Infrastructure.Data;
using PolicyManagement.Infrastructure.Services;

namespace PolicyManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PolicyDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(PolicyDbContext).Assembly.FullName)));

        services.AddScoped<IPolicyService, PolicyService>();

        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
