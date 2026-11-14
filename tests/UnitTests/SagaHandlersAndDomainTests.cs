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
    public void SagaInstance_ShouldStartWithEmptyContextAndAcceptUpdates()
    {
        var saga = new SagaInstance("SO-005");

        Assert.Equal("{}", saga.Context);

        saga.UpdateContext("{\"budgetId\":\"B-1\"}");

        Assert.Equal("{\"budgetId\":\"B-1\"}", saga.Context);
        Assert.Throws<ArgumentException>(() => saga.UpdateContext(" "));
    }

    [Fact]
    public void SagaInstance_ShouldRequireTrigger()
    {
        var saga = new SagaInstance("SO-006");

        Assert.Throws<ArgumentException>(() => saga.TransitionTo(SagaState.WaitingBudget, string.Empty));
    }

    [Fact]
    public async Task ProcessSagaEventCommandHandler_ShouldDelegateToCoordinator()
    {
        var coordinator = new SpySagaCoordinator();
        var handler = new ProcessSagaEventCommandHandler(coordinator);
        var domainEvent = new IntegrationMessage
        {
            CorrelationId = "SO-003",
            Type = "AnyEvent"
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
        public IntegrationMessage? ReceivedEvent { get; private set; }

        public Task HandleEventAsync(IntegrationMessage domainEvent, CancellationToken cancellationToken)
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
