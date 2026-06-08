using Microsoft.EntityFrameworkCore;
using PolicyManagement.Infrastructure;
using PolicyManagement.Infrastructure.Data;
using PolicyManagement.Infrastructure.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);

// TODO (Phase 5): Configure Serilog structured logging here, before any other services.
// Example: builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// TODO (Phase 3/4): Register Application-layer services (AddApplication).
// Example: builder.Services.AddApplication();

// Register Infrastructure-layer services (AddInfrastructure).
// Wires PolicyDbContext (reads ConnectionStrings__DefaultConnection), PolicyService, TimeProvider.
builder.Services.AddInfrastructure(builder.Configuration);

// TODO (Phase 5): Register health checks here.
// Example: builder.Services.AddHealthChecks().AddDbContextCheck<PolicyDbContext>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
        await db.Database.MigrateAsync();
        await PolicyDataSeeder.SeedAsync(db);
        logger.LogInformation("Database migration and seeding completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration or seeding failed. The application will shut down.");
        throw;
    }
}

// TODO (Phase 5): Register global exception handling middleware here, before other middleware.
// Example: app.UseExceptionHandler(); (requires AddProblemDetails / IExceptionHandler registration above)

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

// TODO (Phase 5): Map health check endpoints here.
// Example: app.MapHealthChecks("/health/live", new HealthCheckOptions { ... });
//          app.MapHealthChecks("/health/ready", new HealthCheckOptions { ... });

app.MapControllers();

app.Run();
