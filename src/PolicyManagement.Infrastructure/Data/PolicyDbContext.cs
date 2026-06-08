using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Infrastructure.Data;

public class PolicyDbContext : DbContext
{
    private readonly TimeProvider _timeProvider;

    public PolicyDbContext(DbContextOptions<PolicyDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
    }

    public DbSet<Policy> Policies => Set<Policy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Policy>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.PolicyNumber)
                .IsRequired()
                .HasMaxLength(20);
            entity.HasIndex(p => p.PolicyNumber).IsUnique();

            entity.Property(p => p.PolicyholderName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(p => p.PremiumAmount)
                .HasPrecision(18, 2);

            entity.Property(p => p.LineOfBusiness)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(p => p.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(p => p.Currency)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(p => p.Region)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(p => p.Underwriter)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(p => p.FlaggedForReview)
                .HasDefaultValue(false);

            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired();
        });
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<Policy>())
        {
            if (entry.State == EntityState.Added)
            {
                // Only set CreatedAt when not already assigned — preserves explicit seeder values.
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
