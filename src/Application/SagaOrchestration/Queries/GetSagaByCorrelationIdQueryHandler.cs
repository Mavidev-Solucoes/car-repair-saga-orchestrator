using Application.SagaOrchestration.Abstractions;
using MediatR;

namespace Application.SagaOrchestration.Queries;

public sealed class GetSagaByCorrelationIdQueryHandler(ISagaReadRepository sagaReadRepository)
    : IRequestHandler<GetSagaByCorrelationIdQuery, SagaInstanceResponse?>
{
    public Task<SagaInstanceResponse?> Handle(GetSagaByCorrelationIdQuery request, CancellationToken cancellationToken)
    {
        return sagaReadRepository.GetByCorrelationIdAsync(request.CorrelationId, cancellationToken);
    }
}
