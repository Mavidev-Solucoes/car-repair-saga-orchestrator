using Application.SagaOrchestration;
using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Contracts;
using Application.SagaOrchestration.Messages;
using Domain.Sagas;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests;

public sealed class SagaCoordinatorTests
{
    [Fact]
    public async Task HandleEventAsync_ShouldCreateSagaAndPublishBudgetCommand()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(new DomainEventMessage
        {
            CorrelationId = "SO-001",
            EventType = SagaEventTypes.ServiceOrderOpened,
            Payload = "{\"serviceOrderId\":\"SO-001\"}"
        }, CancellationToken.None);

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);

        Assert.NotNull(saga);
        Assert.Equal(SagaState.WaitingBudget, saga.CurrentState);
        Assert.Single(saga.History);
        Assert.Collection(
            dispatcher.Commands,
            command =>
            {
                Assert.Equal(SagaCommandTypes.CreateBudgetCommand, command.CommandType);
                Assert.Equal("SO-001", command.CorrelationId);
            });
    }

    [Fact]
    public async Task HandleEventAsync_ShouldExecuteHappyPathWorkflow()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetCreated), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetApproved), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.PaymentApproved), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.WorkCompleted), CancellationToken.None);

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);

        Assert.NotNull(saga);
        Assert.Equal(SagaState.Completed, saga.CurrentState);
        Assert.Equal(5, saga.History.Count);
        Assert.Equal(
            [
                SagaCommandTypes.CreateBudgetCommand,
                SagaCommandTypes.ProcessPaymentCommand,
                SagaCommandTypes.CreateWorkOrderCommand,
                SagaCommandTypes.CloseServiceOrderCommand
            ],
            dispatcher.Commands.Select(command => command.CommandType).ToArray());
    }

    [Theory]
    [InlineData(SagaEventTypes.BudgetRejected, SagaCommandTypes.CancelServiceOrderCommand)]
    [InlineData(SagaEventTypes.PaymentRejected, SagaCommandTypes.CancelBudgetCommand)]
    [InlineData(SagaEventTypes.WorkFailed, SagaCommandTypes.CompensateWorkOrderCommand)]
    public async Task HandleEventAsync_ShouldTriggerCompensation(string eventType, string firstCompensationCommand)
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await MoveSagaToExpectedFailureStateAsync(coordinator, eventType);
        dispatcher.Commands.Clear();

        await coordinator.HandleEventAsync(CreateEvent(eventType), CancellationToken.None);

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);

        Assert.NotNull(saga);
        Assert.Equal(SagaState.Failed, saga.CurrentState);
        Assert.Equal(SagaState.Compensating, saga.History[^2].ToState);
        Assert.Equal(SagaState.Failed, saga.History[^1].ToState);
        Assert.Equal(firstCompensationCommand, dispatcher.Commands[0].CommandType);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreUnknownEvents()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened), CancellationToken.None);
        dispatcher.Commands.Clear();

        await coordinator.HandleEventAsync(CreateEvent("UnknownEvent"), CancellationToken.None);

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);

        Assert.NotNull(saga);
        Assert.Equal(SagaState.WaitingBudget, saga.CurrentState);
        Assert.Empty(dispatcher.Commands);
        Assert.Single(saga.History);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreEventsWithoutCorrelationId()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(new DomainEventMessage
        {
            CorrelationId = string.Empty,
            EventType = SagaEventTypes.ServiceOrderOpened
        }, CancellationToken.None);

        Assert.Empty(repository.Items);
        Assert.Empty(dispatcher.Commands);
    }

    private static SagaCoordinator CreateCoordinator(InMemorySagaRepository repository, RecordingCommandDispatcher dispatcher)
    {
        return new SagaCoordinator(repository, dispatcher, NullLogger<SagaCoordinator>.Instance);
    }

    private static DomainEventMessage CreateEvent(string eventType)
    {
        return new DomainEventMessage
        {
            CorrelationId = "SO-001",
            EventType = eventType,
            Payload = $"{{\"eventType\":\"{eventType}\"}}"
        };
    }

    private static async Task MoveSagaToExpectedFailureStateAsync(SagaCoordinator coordinator, string eventType)
    {
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetCreated), CancellationToken.None);

        if (eventType == SagaEventTypes.BudgetRejected)
        {
            return;
        }

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetApproved), CancellationToken.None);
        if (eventType == SagaEventTypes.PaymentRejected)
        {
            return;
        }

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.PaymentApproved), CancellationToken.None);
    }

    private sealed class InMemorySagaRepository : ISagaRepository
    {
        public Dictionary<string, SagaInstance> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<SagaInstance?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
        {
            Items.TryGetValue(correlationId, out var saga);
            return Task.FromResult(saga);
        }

        public Task AddAsync(SagaInstance saga, CancellationToken cancellationToken)
        {
            Items[saga.CorrelationId] = saga;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingCommandDispatcher : ICommandDispatcher
    {
        public List<CommandMessage> Commands { get; } = [];

        public Task DispatchAsync(CommandMessage command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }
}
