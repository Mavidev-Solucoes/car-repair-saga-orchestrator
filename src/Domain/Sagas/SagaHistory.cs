namespace Domain.Sagas;

public sealed class SagaHistory
{
    public long Id { get; private set; }
    public Guid SagaInstanceId { get; private set; }
    public SagaState FromState { get; private set; }
    public SagaState ToState { get; private set; }
    public string Trigger { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public SagaInstance? SagaInstance { get; private set; }

    private SagaHistory()
    {
    }

    public SagaHistory(
        Guid sagaInstanceId,
        SagaState fromState,
        SagaState toState,
        string trigger,
        string? details,
        DateTime occurredAtUtc)
    {
        SagaInstanceId = sagaInstanceId;
        FromState = fromState;
        ToState = toState;
        Trigger = trigger;
        Details = details;
        OccurredAtUtc = occurredAtUtc;
    }
}
