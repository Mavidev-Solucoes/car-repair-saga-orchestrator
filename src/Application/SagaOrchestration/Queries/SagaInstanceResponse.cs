using System.Diagnostics.CodeAnalysis;

namespace Application.SagaOrchestration.Queries;

[ExcludeFromCodeCoverage]
public sealed class SagaInstanceResponse
{
    public Guid Id { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string CurrentState { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public IReadOnlyCollection<SagaHistoryResponse> History { get; init; } = [];
}

[ExcludeFromCodeCoverage]
public sealed class SagaHistoryResponse
{
    public long Id { get; init; }
    public string FromState { get; init; } = string.Empty;
    public string ToState { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public string? Details { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}
