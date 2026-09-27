using System.Diagnostics.CodeAnalysis;

namespace Application.SagaOrchestration.Contracts;

[ExcludeFromCodeCoverage]
public static class SagaEventTypes
{
    public const string ServiceOrderOpened = "ServiceOrderOpened";
    public const string BudgetCreated = "BudgetCreated";
    public const string BudgetApproved = "BudgetApproved";
    public const string BudgetRejected = "BudgetRejected";
    public const string PaymentApproved = "PaymentApproved";
    public const string PaymentRejected = "PaymentRejected";
    public const string WorkCompleted = "WorkCompleted";
    public const string WorkFailed = "WorkFailed";

    public static readonly IReadOnlyCollection<string> All =
    [
        ServiceOrderOpened,
        BudgetCreated,
        BudgetApproved,
        BudgetRejected,
        PaymentApproved,
        PaymentRejected,
        WorkCompleted,
        WorkFailed
    ];
}
