namespace Application.SagaOrchestration.Messages;

public sealed class CommandMessage
{
    public string CommandType { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string? Payload { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
