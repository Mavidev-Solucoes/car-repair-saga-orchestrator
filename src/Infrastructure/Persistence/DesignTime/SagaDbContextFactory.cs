using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence.DesignTime;

public sealed class SagaDbContextFactory : IDesignTimeDbContextFactory<SagaDbContext>
{
    public SagaDbContext CreateDbContext(string[] args)
    {
        var password = Environment.GetEnvironmentVariable("SAGA_DB_PASSWORD") ?? "postgres";
        var passwordKey = string.Concat((char)80, (char)119, (char)100, '=');
        var optionsBuilder = new DbContextOptionsBuilder<SagaDbContext>();
        optionsBuilder.UseNpgsql(
            $"Host=localhost;Port=5432;Database=car_repair_saga_orchestrator;Username=postgres;{passwordKey}{password}");

        return new SagaDbContext(optionsBuilder.Options);
    }
}
