namespace Domain.Sagas;

public sealed class SagaInstance
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string CorrelationId { get; private set; } = string.Empty;
    public SagaState CurrentState { get; private set; } = SagaState.Started;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Objeto JSON com os dados acumulados dos eventos da saga. Vai como payload dos comandos.
    /// </summary>
    public string Context { get; private set; } = "{}";

    public List<SagaHistory> History { get; private set; } = [];

    private SagaInstance()
    {
    }

    public SagaInstance(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation id is required.", nameof(correlationId));
        }

        CorrelationId = correlationId;
    }

    public void UpdateContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new ArgumentException("Context is required.", nameof(context));
        }

        Context = context;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void TransitionTo(SagaState nextState, string trigger, string? details = null, DateTime? occurredAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(trigger))
        {
            throw new ArgumentException("Trigger is required.", nameof(trigger));
        }

        var fromState = CurrentState;
        CurrentState = nextState;
        UpdatedAtUtc = DateTime.UtcNow;

        History.Add(new SagaHistory(
            Id,
            fromState,
            nextState,
            trigger,
            details,
            occurredAtUtc ?? DateTime.UtcNow));
    }
}
