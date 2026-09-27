using Application.SagaOrchestration.Abstractions;
using MediatR;

namespace Application.SagaOrchestration.Commands;

public sealed class ProcessSagaEventCommandHandler(ISagaCoordinator sagaCoordinator) : IRequestHandler<ProcessSagaEventCommand>
{
    public async Task Handle(ProcessSagaEventCommand request, CancellationToken cancellationToken)
    {
        await sagaCoordinator.HandleEventAsync(request.DomainEvent, cancellationToken);
    }
}
