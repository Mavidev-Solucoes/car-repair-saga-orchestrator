using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Queries;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class SagaReadRepository(SagaDbContext dbContext) : ISagaReadRepository
{
    public Task<SagaInstanceResponse?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
    {
        return dbContext.SagaInstances
            .AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .Select(x => new SagaInstanceResponse
            {
                Id = x.Id,
                CorrelationId = x.CorrelationId,
                CurrentState = x.CurrentState.ToString(),
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc,
                History = x.History
                    .OrderBy(entry => entry.OccurredAtUtc)
                    .Select(entry => new SagaHistoryResponse
                    {
                        Id = entry.Id,
                        FromState = entry.FromState.ToString(),
                        ToState = entry.ToState.ToString(),
                        Trigger = entry.Trigger,
                        Details = entry.Details,
                        OccurredAtUtc = entry.OccurredAtUtc
                    })
                    .ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
