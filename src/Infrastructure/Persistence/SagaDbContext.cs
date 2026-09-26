using Domain.Sagas;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class SagaDbContext(DbContextOptions<SagaDbContext> options) : DbContext(options)
{
    public DbSet<SagaInstance> SagaInstances => Set<SagaInstance>();
    public DbSet<SagaHistory> SagaHistory => Set<SagaHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SagaInstance>(builder =>
        {
            builder.ToTable("saga_instances");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(120);
            builder.HasIndex(x => x.CorrelationId).IsUnique();
            builder.Property(x => x.CurrentState).IsRequired().HasConversion<string>().HasMaxLength(40);
            builder.Property(x => x.CreatedAtUtc).IsRequired();
            builder.Property(x => x.UpdatedAtUtc).IsRequired();
            builder.HasMany(x => x.History)
                .WithOne(x => x.SagaInstance)
                .HasForeignKey(x => x.SagaInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SagaHistory>(builder =>
        {
            builder.ToTable("saga_history");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedOnAdd();
            builder.Property(x => x.FromState).IsRequired().HasConversion<string>().HasMaxLength(40);
            builder.Property(x => x.ToState).IsRequired().HasConversion<string>().HasMaxLength(40);
            builder.Property(x => x.Trigger).IsRequired().HasMaxLength(100);
            builder.Property(x => x.Details).HasColumnType("text");
            builder.Property(x => x.OccurredAtUtc).IsRequired();
            builder.HasIndex(x => new { x.SagaInstanceId, x.OccurredAtUtc });
        });
    }
}
