using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Messages;
using Domain.Sagas;
using Microsoft.Extensions.Logging;

namespace Application.SagaOrchestration;

public sealed class SagaCoordinator(
    ISagaRepository sagaRepository,
    ICommandDispatcher commandDispatcher,
    ILogger<SagaCoordinator> logger) : ISagaCoordinator
{
    private const string SagaStartEvent = "RepairOrderCreated";

    private static readonly Dictionary<string, TransitionDefinition> ForwardTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RepairOrderCreated"] = new(SagaState.Started, SagaState.WaitingBudget, "RequestBudget"),
            ["BudgetProvided"] = new(SagaState.WaitingBudget, SagaState.WaitingBudgetApproval, "RequestBudgetApproval"),
            ["BudgetApproved"] = new(SagaState.WaitingBudgetApproval, SagaState.WaitingPayment, "RequestPayment"),
            ["PaymentConfirmed"] = new(SagaState.WaitingPayment, SagaState.WaitingProduction, "StartProduction"),
            ["ProductionCompleted"] = new(SagaState.WaitingProduction, SagaState.Completed, null)
        };

    private static readonly HashSet<string> FailureEvents =
    [
        "BudgetRejected",
        "PaymentFailed",
        "ProductionFailed"
    ];

    public async Task HandleEventAsync(DomainEventMessage domainEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domainEvent.CorrelationId))
        {
            logger.LogWarning("Skipping event {EventType} because CorrelationId is missing.", domainEvent.EventType);
            return;
        }

        var saga = await sagaRepository.GetByCorrelationIdAsync(domainEvent.CorrelationId, cancellationToken);

        if (saga is null)
        {
            if (!domainEvent.EventType.Equals(SagaStartEvent, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Ignoring event {EventType} because saga for correlation {CorrelationId} does not exist yet.",
                    domainEvent.EventType,
                    domainEvent.CorrelationId);
                return;
            }

            saga = new SagaInstance(domainEvent.CorrelationId);
            await sagaRepository.AddAsync(saga, cancellationToken);
        }

        if (FailureEvents.Contains(domainEvent.EventType))
        {
            await StartCompensationAsync(saga, domainEvent, cancellationToken);
            return;
        }

        if (!ForwardTransitions.TryGetValue(domainEvent.EventType, out var transition))
        {
            logger.LogInformation("Ignoring unmapped event {EventType} for saga {SagaId}.", domainEvent.EventType, saga.Id);
            return;
        }

        if (saga.CurrentState != transition.ExpectedCurrentState)
        {
            logger.LogWarning(
                "Skipping transition for saga {SagaId}. Event {EventType} expected state {Expected} but current is {Current}.",
                saga.Id,
                domainEvent.EventType,
                transition.ExpectedCurrentState,
                saga.CurrentState);
            return;
        }

        saga.TransitionTo(
            transition.NextState,
            domainEvent.EventType,
            domainEvent.Payload,
            domainEvent.OccurredAtUtc);

        if (!string.IsNullOrWhiteSpace(transition.CommandType))
        {
            await commandDispatcher.DispatchAsync(
                new CommandMessage
                {
                    CommandType = transition.CommandType,
                    CorrelationId = saga.CorrelationId,
                    Payload = domainEvent.Payload
                },
                cancellationToken);
        }

        await sagaRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task StartCompensationAsync(
        SagaInstance saga,
        DomainEventMessage domainEvent,
        CancellationToken cancellationToken)
    {
        if (saga.CurrentState is SagaState.Completed or SagaState.Failed)
        {
            return;
        }

        var failedState = saga.CurrentState;
        saga.TransitionTo(SagaState.Compensating, domainEvent.EventType, domainEvent.Payload, domainEvent.OccurredAtUtc);

        foreach (var command in BuildCompensationCommands(failedState))
        {
            await commandDispatcher.DispatchAsync(
                new CommandMessage
                {
                    CommandType = command,
                    CorrelationId = saga.CorrelationId,
                    Payload = domainEvent.Payload
                },
                cancellationToken);
        }

        saga.TransitionTo(SagaState.Failed, "CompensationTriggered", domainEvent.Payload, DateTime.UtcNow);
        await sagaRepository.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<string> BuildCompensationCommands(SagaState failedState)
    {
        return failedState switch
        {
            SagaState.WaitingProduction => ["RevertProduction", "RefundPayment", "CancelBudget"],
            SagaState.WaitingPayment => ["RefundPayment", "CancelBudget"],
            SagaState.WaitingBudgetApproval => ["CancelBudget"],
            SagaState.WaitingBudget => ["CancelBudgetRequest"],
            _ => ["CancelRepairOrder"]
        };
    }

    private sealed record TransitionDefinition(
        SagaState ExpectedCurrentState,
        SagaState NextState,
        string? CommandType);
}
