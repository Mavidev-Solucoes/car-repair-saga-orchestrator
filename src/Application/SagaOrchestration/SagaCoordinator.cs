using System.Text.Json.Nodes;
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

    public async Task HandleEventAsync(IntegrationMessage domainEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domainEvent.CorrelationId))
        {
            logger.LogWarning("Skipping event {EventType} because CorrelationId is missing.", domainEvent.Type);
            return;
        }

        if (string.IsNullOrWhiteSpace(domainEvent.Type))
        {
            logger.LogWarning("Skipping event because Type is missing for correlation {CorrelationId}.", domainEvent.CorrelationId);
            return;
        }

        var saga = await sagaRepository.GetByCorrelationIdAsync(domainEvent.CorrelationId, cancellationToken);

        if (saga is null)
        {
            if (!domainEvent.Type.Equals(SagaEventTypes.ServiceOrderOpened, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Ignoring event {EventType} because saga for correlation {CorrelationId} does not exist yet.",
                    domainEvent.Type,
                    domainEvent.CorrelationId);
                return;
            }

            saga = new SagaInstance(domainEvent.CorrelationId);
            await sagaRepository.AddAsync(saga, cancellationToken);
        }

        if (CompensationTransitions.TryGetValue(domainEvent.Type, out var compensation))
        {
            await StartCompensationAsync(saga, domainEvent, compensation, cancellationToken);
            return;
        }

        if (!ForwardTransitions.TryGetValue(domainEvent.Type, out var transition))
        {
            logger.LogInformation("Ignoring unmapped event {EventType} for saga {SagaId}.", domainEvent.Type, saga.Id);
            return;
        }

        if (saga.CurrentState != transition.ExpectedCurrentState)
        {
            logger.LogWarning(
                "Skipping transition for saga {SagaId}. Event {EventType} expected state {Expected} but current is {Current}.",
                saga.Id,
                domainEvent.Type,
                transition.ExpectedCurrentState,
                saga.CurrentState);
            return;
        }

        var context = MergeContext(saga, domainEvent);

        saga.TransitionTo(
            transition.NextState,
            domainEvent.Type,
            SerializePayload(domainEvent),
            domainEvent.OccurredAtUtc);

        if (!string.IsNullOrWhiteSpace(transition.CommandType))
        {
            await commandDispatcher.DispatchAsync(BuildCommand(transition.CommandType, saga, context), cancellationToken);
        }

        await sagaRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task StartCompensationAsync(
        SagaInstance saga,
        IntegrationMessage domainEvent,
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
                domainEvent.Type,
                compensation.ExpectedCurrentState,
                saga.CurrentState);
            return;
        }

        var details = SerializePayload(domainEvent);
        var context = MergeContext(saga, domainEvent);

        if (!isAlreadyCompensating)
        {
            saga.TransitionTo(SagaState.Compensating, domainEvent.Type, details, domainEvent.OccurredAtUtc);
            await sagaRepository.SaveChangesAsync(cancellationToken);
        }

        foreach (var command in compensation.CommandTypes)
        {
            if (HasCompensationDispatchMarker(saga, command))
            {
                continue;
            }

            await commandDispatcher.DispatchAsync(BuildCommand(command, saga, context), cancellationToken);

            saga.TransitionTo(
                SagaState.Compensating,
                BuildCompensationDispatchMarker(command),
                details,
                domainEvent.OccurredAtUtc);

            await sagaRepository.SaveChangesAsync(cancellationToken);
        }

        saga.TransitionTo(SagaState.Failed, "CompensationTriggered", details, domainEvent.OccurredAtUtc);
        await sagaRepository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Junta o payload do evento no contexto da saga (a chave mais recente vence) e devolve o resultado.
    /// </summary>
    private static JsonObject MergeContext(SagaInstance saga, IntegrationMessage domainEvent)
    {
        var context = JsonNode.Parse(saga.Context) as JsonObject ?? [];

        if (domainEvent.Payload is not null)
        {
            foreach (var (key, value) in domainEvent.Payload)
            {
                context[key] = value?.DeepClone();
            }
        }

        saga.UpdateContext(context.ToJsonString());
        return context;
    }

    private static IntegrationMessage BuildCommand(string commandType, SagaInstance saga, JsonObject context)
    {
        return new IntegrationMessage
        {
            Type = commandType,
            CorrelationId = saga.CorrelationId,
            OccurredAtUtc = DateTime.UtcNow,
            Payload = (JsonObject)context.DeepClone()
        };
    }

    private static string? SerializePayload(IntegrationMessage domainEvent) => domainEvent.Payload?.ToJsonString();

    private static bool HasCompensationDispatchMarker(SagaInstance saga, string commandType)
    {
        var marker = BuildCompensationDispatchMarker(commandType);
        return saga.History.Any(entry => entry.Trigger.Equals(marker, StringComparison.Ordinal));
    }

    private static string BuildCompensationDispatchMarker(string commandType) => $"CompensationCommandDispatched:{commandType}";

    private sealed record TransitionDefinition(
        SagaState ExpectedCurrentState,
        SagaState NextState,
        string? CommandType);

    private sealed record CompensationDefinition(
        SagaState ExpectedCurrentState,
        IReadOnlyCollection<string> CommandTypes);
}
