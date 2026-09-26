using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class DatabaseInitializationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();

            var hasMigrations = dbContext.Database.GetMigrations().Any();
            if (!hasMigrations)
            {
                throw new InvalidOperationException(
                    "No EF Core migrations were found for SagaDbContext. Create and apply migrations before starting the service.");
            }

            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Database migration could not be applied during startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
