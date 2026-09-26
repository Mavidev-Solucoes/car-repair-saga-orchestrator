using Application.SagaOrchestration.Queries;

namespace Application.SagaOrchestration.Abstractions;

public interface ISagaReadRepository
{
    Task<SagaInstanceResponse?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken);
}
