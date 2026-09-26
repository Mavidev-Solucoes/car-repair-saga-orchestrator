using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Persistence;

public sealed class DatabaseInitializationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
