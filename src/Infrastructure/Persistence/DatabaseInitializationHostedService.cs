using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class DatabaseInitializationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
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

                await dbContext.Database.MigrateAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Database migration could not be applied during startup. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
