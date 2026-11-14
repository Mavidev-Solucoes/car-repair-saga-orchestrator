using System.Text.Json.Nodes;

namespace Application.SagaOrchestration.Messages;

/// <summary>
/// Envelope único de eventos e comandos, conforme docs/contrato-mensageria.md.
/// </summary>
public sealed class IntegrationMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public JsonObject? Payload { get; init; }
}
