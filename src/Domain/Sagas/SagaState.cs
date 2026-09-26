namespace Domain.Sagas;

public enum SagaState
{
    Started = 1,
    WaitingBudget = 2,
    WaitingBudgetApproval = 3,
    WaitingPayment = 4,
    WaitingProduction = 5,
    Completed = 6,
    Compensating = 7,
    Failed = 8
}
