using MediatR;

namespace Application.SagaOrchestration.Queries;

public sealed record GetSagaByCorrelationIdQuery(string CorrelationId) : IRequest<SagaInstanceResponse?>;
