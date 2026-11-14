using System.Text.Json.Nodes;
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

        await coordinator.HandleEventAsync(new IntegrationMessage
        {
            CorrelationId = "SO-001",
            Type = SagaEventTypes.ServiceOrderOpened,
            Payload = new JsonObject { ["serviceOrderId"] = "SO-001" }
        }, CancellationToken.None);

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);

        Assert.NotNull(saga);
        Assert.Equal(SagaState.WaitingBudget, saga.CurrentState);
        Assert.Single(saga.History);
        Assert.Collection(
            dispatcher.Commands,
            command =>
            {
                Assert.Equal(SagaCommandTypes.CreateBudgetCommand, command.Type);
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
            dispatcher.Commands.Select(command => command.Type).ToArray());
    }

    [Theory]
    [InlineData(SagaEventTypes.BudgetRejected, new[] { SagaCommandTypes.CancelServiceOrderCommand })]
    [InlineData(SagaEventTypes.PaymentRejected, new[] { SagaCommandTypes.CancelBudgetCommand, SagaCommandTypes.CancelServiceOrderCommand })]
    [InlineData(SagaEventTypes.WorkFailed, new[] { SagaCommandTypes.CancelBudgetCommand, SagaCommandTypes.CancelServiceOrderCommand })]
    public async Task HandleEventAsync_ShouldTriggerCompensation(string eventType, string[] expectedCommands)
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
        Assert.Equal(expectedCommands, dispatcher.Commands.Select(command => command.Type).ToArray());
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

        await coordinator.HandleEventAsync(new IntegrationMessage
        {
            CorrelationId = string.Empty,
            Type = SagaEventTypes.ServiceOrderOpened
        }, CancellationToken.None);

        Assert.Empty(repository.Items);
        Assert.Empty(dispatcher.Commands);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreEventsWithoutEventType()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(new IntegrationMessage
        {
            CorrelationId = "SO-001",
            Type = string.Empty
        }, CancellationToken.None);

        Assert.Empty(repository.Items);
        Assert.Empty(dispatcher.Commands);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldResumeCompensationWithoutRedispatchingRecordedCommands()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);
        var saga = new SagaInstance("SO-001");
        saga.TransitionTo(SagaState.WaitingBudget, SagaEventTypes.ServiceOrderOpened);
        saga.TransitionTo(SagaState.WaitingBudgetApproval, SagaEventTypes.BudgetCreated);
        saga.TransitionTo(SagaState.WaitingPayment, SagaEventTypes.BudgetApproved);
        saga.TransitionTo(SagaState.WaitingProduction, SagaEventTypes.PaymentApproved);
        saga.TransitionTo(SagaState.Compensating, SagaEventTypes.WorkFailed);
        saga.TransitionTo(SagaState.Compensating, "CompensationCommandDispatched:CancelBudgetCommand");
        repository.Items[saga.CorrelationId] = saga;

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.WorkFailed), CancellationToken.None);

        Assert.Equal(SagaState.Failed, saga.CurrentState);
        Assert.Equal(
            [SagaCommandTypes.CancelServiceOrderCommand],
            dispatcher.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public async Task HandleEventAsync_ShouldSendAccumulatedContextAsCommandPayload()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(
            SagaEventTypes.ServiceOrderOpened,
            new JsonObject
            {
                ["serviceOrderId"] = "SO-001",
                ["customerName"] = "Maria",
                ["items"] = new JsonArray(new JsonObject { ["description"] = "Troca de óleo", ["unitPrice"] = 150m, ["quantity"] = 1 })
            }), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetCreated, new JsonObject { ["budgetId"] = "B-1", ["totalAmount"] = 150m }), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetApproved, new JsonObject { ["budgetId"] = "B-1", ["totalAmount"] = 150m }), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.PaymentApproved, new JsonObject { ["paymentId"] = "P-1", ["amount"] = 150m }), CancellationToken.None);

        var createWorkOrder = dispatcher.Commands.Single(command => command.Type == SagaCommandTypes.CreateWorkOrderCommand);
        Assert.Equal("SO-001", createWorkOrder.CorrelationId);
        Assert.NotNull(createWorkOrder.Payload);
        Assert.Equal("Maria", createWorkOrder.Payload["customerName"]!.GetValue<string>());
        Assert.Equal("B-1", createWorkOrder.Payload["budgetId"]!.GetValue<string>());
        Assert.Equal("P-1", createWorkOrder.Payload["paymentId"]!.GetValue<string>());
        Assert.Single(createWorkOrder.Payload["items"]!.AsArray());

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);
        Assert.Contains("\"paymentId\":\"P-1\"", saga!.Context);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldSendContextToCompensationCommands()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened, new JsonObject { ["serviceOrderId"] = "SO-001" }), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetCreated, new JsonObject { ["budgetId"] = "B-1" }), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetApproved, new JsonObject { ["budgetId"] = "B-1" }), CancellationToken.None);
        dispatcher.Commands.Clear();

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.PaymentRejected, new JsonObject { ["paymentId"] = "P-1", ["reason"] = "Cartão recusado" }), CancellationToken.None);

        var cancelBudget = dispatcher.Commands.First();
        Assert.Equal(SagaCommandTypes.CancelBudgetCommand, cancelBudget.Type);
        Assert.Equal("B-1", cancelBudget.Payload!["budgetId"]!.GetValue<string>());
        Assert.Equal("Cartão recusado", cancelBudget.Payload["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreRepeatedEventWithoutRedispatching()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened), CancellationToken.None);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened), CancellationToken.None);

        Assert.Single(dispatcher.Commands);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreEventsForUnknownSaga()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.BudgetCreated), CancellationToken.None);

        Assert.Empty(repository.Items);
        Assert.Empty(dispatcher.Commands);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreCompensationOutOfExpectedState()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.ServiceOrderOpened), CancellationToken.None);
        dispatcher.Commands.Clear();

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.WorkFailed), CancellationToken.None);

        var saga = await repository.GetByCorrelationIdAsync("SO-001", CancellationToken.None);
        Assert.Equal(SagaState.WaitingBudget, saga!.CurrentState);
        Assert.Empty(dispatcher.Commands);
    }

    [Fact]
    public async Task HandleEventAsync_ShouldIgnoreCompensationAfterSagaFinished()
    {
        var repository = new InMemorySagaRepository();
        var dispatcher = new RecordingCommandDispatcher();
        var coordinator = CreateCoordinator(repository, dispatcher);

        await MoveSagaToExpectedFailureStateAsync(coordinator, SagaEventTypes.WorkFailed);
        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.WorkCompleted), CancellationToken.None);
        dispatcher.Commands.Clear();

        await coordinator.HandleEventAsync(CreateEvent(SagaEventTypes.WorkFailed), CancellationToken.None);

        Assert.Empty(dispatcher.Commands);
    }

    private static IntegrationMessage CreateEvent(string eventType, JsonObject payload)
    {
        return new IntegrationMessage
        {
            CorrelationId = "SO-001",
            Type = eventType,
            Payload = payload
        };
    }

    private static SagaCoordinator CreateCoordinator(InMemorySagaRepository repository, RecordingCommandDispatcher dispatcher)
    {
        return new SagaCoordinator(repository, dispatcher, NullLogger<SagaCoordinator>.Instance);
    }

    private static IntegrationMessage CreateEvent(string eventType)
    {
        return new IntegrationMessage
        {
            CorrelationId = "SO-001",
            Type = eventType,
            Payload = new JsonObject { ["lastEvent"] = eventType }
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
        public List<IntegrationMessage> Commands { get; } = [];

        public Task DispatchAsync(IntegrationMessage command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }
}
