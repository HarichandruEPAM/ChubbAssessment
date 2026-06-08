using System.Reflection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using PolicyManagement.API.HealthChecks;
using PolicyManagement.API.Validation;
using PolicyManagement.Infrastructure;
using PolicyManagement.Infrastructure.Data;
using PolicyManagement.Infrastructure.Data.Seeding;
using Serilog;
using Serilog.Formatting.Compact;

// ---------------------------------------------------------------------------
// Task 5.6 — Serilog: configure before builder so bootstrap errors are logged.
// ---------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Task 5.6 — Replace default logging with Serilog structured logging.
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .WriteTo.Console(new CompactJsonFormatter()));

    // Task 5.4 — Problem Details support (RFC 7807) for all error responses.
    builder.Services.AddProblemDetails();

    // Task 5.2 — Controllers with custom validation error factory (Problem Details).
    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problemDetails = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Instance = context.HttpContext.Request.Path
                };
                return new BadRequestObjectResult(problemDetails)
                {
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

    // Task 5.7 — Swagger / OpenAPI.
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Policy Management BFF API",
            Version = "v1",
            Description = "Backend-for-Frontend service for Chubb APAC policy data."
        });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            options.IncludeXmlComments(xmlPath);
    });

    // Register Infrastructure (DbContext, PolicyService, TimeProvider).
    builder.Services.AddInfrastructure(builder.Configuration);

    // Task 5.5 — Health checks: liveness (no checks) + readiness (DB connectivity).
    // DatabaseHealthCheck uses IServiceScopeFactory internally to resolve the
    // scoped PolicyDbContext, so it can be safely registered as a singleton check.
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database");

    // Task 5.3 — Register validation filter for PolicyListRequest.
    builder.Services.AddSingleton<PolicyListValidationFilter>();

    var app = builder.Build();

    // ---------------------------------------------------------------------------
    // Startup migration and seed (Phase 4 — retained from original Program.cs).
    // ---------------------------------------------------------------------------
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

    // ---------------------------------------------------------------------------
    // Middleware pipeline — order matters.
    // ---------------------------------------------------------------------------

    // Task 5.4 — Global exception handler: maps unhandled exceptions to Problem Details.
    app.UseExceptionHandler();

    // Task 5.6 — Serilog request logging (after exception handler, before routing).
    app.UseSerilogRequestLogging();

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    // Task 5.5 — Health check endpoints.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false  // Liveness: process-up only, no checks.
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Name == "database"
    });

    // Task 5.7 — Swagger UI (Development only).
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Policy Management BFF API v1");
            options.RoutePrefix = "swagger";
        });
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
