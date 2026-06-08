var builder = WebApplication.CreateBuilder(args);

// TODO (Phase 5): Configure Serilog structured logging here, before any other services.
// Example: builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// TODO (Phase 3/4): Register Application-layer services (AddApplication).
// Example: builder.Services.AddApplication();

// TODO (Phase 3/4): Register Infrastructure-layer services (AddInfrastructure).
// Wires PolicyDbContext (reads ConnectionStrings__DefaultConnection), PolicyService, TimeProvider.
// Example: builder.Services.AddInfrastructure(builder.Configuration);

// TODO (Phase 5): Register health checks here.
// Example: builder.Services.AddHealthChecks().AddDbContextCheck<PolicyDbContext>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
