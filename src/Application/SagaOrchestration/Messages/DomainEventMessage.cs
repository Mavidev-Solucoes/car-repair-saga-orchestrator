namespace Application.SagaOrchestration.Messages;

public sealed class DomainEventMessage
{
    public string EventType { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string? Payload { get; init; }
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
