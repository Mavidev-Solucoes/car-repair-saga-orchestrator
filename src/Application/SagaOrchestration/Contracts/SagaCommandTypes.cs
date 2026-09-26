namespace Application.SagaOrchestration.Contracts;

public static class SagaCommandTypes
{
    public const string CreateBudgetCommand = "CreateBudgetCommand";
    public const string ProcessPaymentCommand = "ProcessPaymentCommand";
    public const string CreateWorkOrderCommand = "CreateWorkOrderCommand";
    public const string CloseServiceOrderCommand = "CloseServiceOrderCommand";
    public const string CancelServiceOrderCommand = "CancelServiceOrderCommand";
    public const string CancelBudgetCommand = "CancelBudgetCommand";
    public const string CompensateWorkOrderCommand = "CompensateWorkOrderCommand";
    public const string ReturnServiceOrderToApprovedCommand = "ReturnServiceOrderToApprovedCommand";
}
