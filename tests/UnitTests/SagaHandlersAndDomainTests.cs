using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Commands;
using Application.SagaOrchestration.Messages;
using Application.SagaOrchestration.Queries;
using Domain.Sagas;

namespace UnitTests;

public sealed class SagaHandlersAndDomainTests
{
    [Fact]
    public void SagaInstance_ShouldRequireCorrelationId()
    {
        Assert.Throws<ArgumentException>(() => new SagaInstance(string.Empty));
    }

    [Fact]
    public void SagaInstance_ShouldRecordHistoryOnTransition()
    {
        var saga = new SagaInstance("SO-002");

        saga.TransitionTo(SagaState.WaitingBudget, "ServiceOrderOpened", "{}", DateTime.UtcNow);

        Assert.Single(saga.History);
        Assert.Equal(SagaState.Started, saga.History[0].FromState);
        Assert.Equal(SagaState.WaitingBudget, saga.History[0].ToState);
    }

    [Fact]
    public async Task ProcessSagaEventCommandHandler_ShouldDelegateToCoordinator()
    {
        var coordinator = new SpySagaCoordinator();
        var handler = new ProcessSagaEventCommandHandler(coordinator);
        var domainEvent = new DomainEventMessage
        {
            CorrelationId = "SO-003",
            EventType = "AnyEvent"
        };

        await handler.Handle(new ProcessSagaEventCommand(domainEvent), CancellationToken.None);

        Assert.Same(domainEvent, coordinator.ReceivedEvent);
    }

    [Fact]
    public async Task GetSagaByCorrelationIdQueryHandler_ShouldReturnRepositoryResult()
    {
        var expected = new SagaInstanceResponse
        {
            CorrelationId = "SO-004",
            CurrentState = SagaState.WaitingPayment.ToString()
        };

        var handler = new GetSagaByCorrelationIdQueryHandler(new StubSagaReadRepository(expected));

        var result = await handler.Handle(new GetSagaByCorrelationIdQuery("SO-004"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    private sealed class SpySagaCoordinator : ISagaCoordinator
    {
        public DomainEventMessage? ReceivedEvent { get; private set; }

        public Task HandleEventAsync(DomainEventMessage domainEvent, CancellationToken cancellationToken)
        {
            ReceivedEvent = domainEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSagaReadRepository(SagaInstanceResponse response) : ISagaReadRepository
    {
        public Task<SagaInstanceResponse?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult<SagaInstanceResponse?>(response);
        }
    }
}
