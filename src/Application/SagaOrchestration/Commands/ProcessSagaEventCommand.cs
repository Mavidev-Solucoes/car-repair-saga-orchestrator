using Application.SagaOrchestration.Messages;
using MediatR;

namespace Application.SagaOrchestration.Commands;

public sealed record ProcessSagaEventCommand(DomainEventMessage DomainEvent) : IRequest;
