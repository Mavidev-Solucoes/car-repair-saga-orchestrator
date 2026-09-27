using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Contracts;
using Application.SagaOrchestration.Messages;
using Domain.Sagas;
using Microsoft.Extensions.Logging;

namespace Application.SagaOrchestration;

public sealed class SagaCoordinator(
    ISagaRepository sagaRepository,
    ICommandDispatcher commandDispatcher,
    ILogger<SagaCoordinator> logger) : ISagaCoordinator
{
    private static readonly Dictionary<string, TransitionDefinition> ForwardTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SagaEventTypes.ServiceOrderOpened] = new(SagaState.Started, SagaState.WaitingBudget, SagaCommandTypes.CreateBudgetCommand),
            [SagaEventTypes.BudgetCreated] = new(SagaState.WaitingBudget, SagaState.WaitingBudgetApproval, null),
            [SagaEventTypes.BudgetApproved] = new(SagaState.WaitingBudgetApproval, SagaState.WaitingPayment, SagaCommandTypes.ProcessPaymentCommand),
            [SagaEventTypes.PaymentApproved] = new(SagaState.WaitingPayment, SagaState.WaitingProduction, SagaCommandTypes.CreateWorkOrderCommand),
            [SagaEventTypes.WorkCompleted] = new(SagaState.WaitingProduction, SagaState.Completed, SagaCommandTypes.CloseServiceOrderCommand)
        };

    private static readonly Dictionary<string, CompensationDefinition> CompensationTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SagaEventTypes.BudgetRejected] = new(SagaState.WaitingBudgetApproval, [SagaCommandTypes.CancelServiceOrderCommand]),
            [SagaEventTypes.PaymentRejected] = new(
                SagaState.WaitingPayment,
                [SagaCommandTypes.CancelBudgetCommand, SagaCommandTypes.CancelServiceOrderCommand]),
            [SagaEventTypes.WorkFailed] = new(
                SagaState.WaitingProduction,
                [SagaCommandTypes.CancelBudgetCommand, SagaCommandTypes.CancelServiceOrderCommand])
        };

    public async Task HandleEventAsync(DomainEventMessage domainEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domainEvent.CorrelationId))
        {
            logger.LogWarning("Skipping event {EventType} because CorrelationId is missing.", domainEvent.EventType);
            return;
        }

        if (string.IsNullOrWhiteSpace(domainEvent.EventType))
        {
            logger.LogWarning("Skipping event because EventType is missing for correlation {CorrelationId}.", domainEvent.CorrelationId);
            return;
        }

        var saga = await sagaRepository.GetByCorrelationIdAsync(domainEvent.CorrelationId, cancellationToken);

        if (saga is null)
        {
            if (!domainEvent.EventType.Equals(SagaEventTypes.ServiceOrderOpened, StringComparison.OrdinalIgnoreCase))
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

        if (CompensationTransitions.TryGetValue(domainEvent.EventType, out var compensation))
        {
            await StartCompensationAsync(saga, domainEvent, compensation, cancellationToken);
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
        CompensationDefinition compensation,
        CancellationToken cancellationToken)
    {
        if (saga.CurrentState is SagaState.Completed or SagaState.Failed)
        {
            return;
        }

        var isAlreadyCompensating = saga.CurrentState == SagaState.Compensating;

        if (!isAlreadyCompensating && saga.CurrentState != compensation.ExpectedCurrentState)
        {
            logger.LogWarning(
                "Skipping compensation for saga {SagaId}. Event {EventType} expected state {Expected} but current is {Current}.",
                saga.Id,
                domainEvent.EventType,
                compensation.ExpectedCurrentState,
                saga.CurrentState);
            return;
        }

        if (!isAlreadyCompensating)
        {
            saga.TransitionTo(SagaState.Compensating, domainEvent.EventType, domainEvent.Payload, domainEvent.OccurredAtUtc);
        }

        foreach (var command in compensation.CommandTypes)
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

        saga.TransitionTo(SagaState.Failed, "CompensationTriggered", domainEvent.Payload, domainEvent.OccurredAtUtc);
        await sagaRepository.SaveChangesAsync(cancellationToken);
    }

    private sealed record TransitionDefinition(
        SagaState ExpectedCurrentState,
        SagaState NextState,
        string? CommandType);

    private sealed record CompensationDefinition(
        SagaState ExpectedCurrentState,
        IReadOnlyCollection<string> CommandTypes);
}
