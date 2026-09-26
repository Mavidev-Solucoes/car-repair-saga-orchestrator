using Application.SagaOrchestration.Messages;

namespace Application.SagaOrchestration.Abstractions;

public interface ICommandDispatcher
{
    Task DispatchAsync(CommandMessage command, CancellationToken cancellationToken);
}
