using Application.SagaOrchestration.Abstractions;
using Domain.Sagas;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class SagaRepository(SagaDbContext dbContext) : ISagaRepository
{
    public Task<SagaInstance?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
    {
        return dbContext.SagaInstances
            .Include(x => x.History)
            .SingleOrDefaultAsync(x => x.CorrelationId == correlationId, cancellationToken);
    }

    public async Task AddAsync(SagaInstance saga, CancellationToken cancellationToken)
    {
        await dbContext.SagaInstances.AddAsync(saga, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
