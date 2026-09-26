using Domain.Sagas;

namespace Application.SagaOrchestration.Abstractions;

public interface ISagaRepository
{
    Task<SagaInstance?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken);
    Task AddAsync(SagaInstance saga, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
