using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence.DesignTime;

public sealed class SagaDbContextFactory : IDesignTimeDbContextFactory<SagaDbContext>
{
    public SagaDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SagaDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=car_repair_saga_orchestrator;Username=postgres;******");

        return new SagaDbContext(optionsBuilder.Options);
    }
}
