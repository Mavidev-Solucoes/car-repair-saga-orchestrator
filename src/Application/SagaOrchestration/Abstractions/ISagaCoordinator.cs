using Application.SagaOrchestration.Messages;

namespace Application.SagaOrchestration.Abstractions;

public interface ISagaCoordinator
{
    Task HandleEventAsync(IntegrationMessage domainEvent, CancellationToken cancellationToken);
}
