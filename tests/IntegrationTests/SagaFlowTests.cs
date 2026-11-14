using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Application.SagaOrchestration.Queries;

namespace IntegrationTests;

[Collection(SagaCollection.Name)]
public sealed class SagaFlowTests(SagaFixture fixture)
{
    [Fact]
    public async Task HappyPath_ShouldOrchestrateUntilCompletedCarryingContext()
    {
        await fixture.WaitForConsumerAsync();
        using var probe = new CommandsProbe(fixture);
        using var client = fixture.Factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString("D");
        var budgetId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        fixture.PublishEvent("ServiceOrderOpened", correlationId, new JsonObject
        {
            ["serviceOrderId"] = correlationId,
            ["customerName"] = "Maria Silva",
            ["items"] = new JsonArray(new JsonObject { ["description"] = "Troca de óleo", ["unitPrice"] = 150m, ["quantity"] = 1 })
        });
        var createBudget = await probe.WaitForAsync("CreateBudgetCommand", correlationId);
        Assert.Equal("Maria Silva", createBudget.Payload!["customerName"]!.GetValue<string>());

        fixture.PublishEvent("BudgetCreated", correlationId, new JsonObject { ["budgetId"] = budgetId, ["totalAmount"] = 150m });
        await WaitForStateAsync(client, correlationId, "WaitingBudgetApproval");
        fixture.PublishEvent("BudgetApproved", correlationId, new JsonObject { ["budgetId"] = budgetId, ["simulatePaymentFailure"] = false });
        var processPayment = await probe.WaitForAsync("ProcessPaymentCommand", correlationId);
        Assert.Equal(budgetId, processPayment.Payload!["budgetId"]!.GetValue<Guid>());

        fixture.PublishEvent("PaymentApproved", correlationId, new JsonObject { ["paymentId"] = paymentId, ["budgetId"] = budgetId, ["amount"] = 150m });
        var createWorkOrder = await probe.WaitForAsync("CreateWorkOrderCommand", correlationId);
        Assert.Single(createWorkOrder.Payload!["items"]!.AsArray());
        Assert.Equal(paymentId, createWorkOrder.Payload["paymentId"]!.GetValue<Guid>());

        fixture.PublishEvent("WorkCompleted", correlationId, new JsonObject { ["workOrderId"] = Guid.NewGuid() });
        await probe.WaitForAsync("CloseServiceOrderCommand", correlationId);

        var saga = await WaitForStateAsync(client, correlationId, "Completed");
        Assert.Contains("Maria Silva", saga.Context);
        Assert.Equal(5, saga.History.Count);
        Assert.Equal("WorkCompleted", saga.History.Last().Trigger);
    }

    [Fact]
    public async Task PaymentRejected_ShouldCompensateBudgetAndServiceOrder()
    {
        await fixture.WaitForConsumerAsync();
        using var probe = new CommandsProbe(fixture);
        using var client = fixture.Factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString("D");
        var budgetId = Guid.NewGuid();

        fixture.PublishEvent("ServiceOrderOpened", correlationId, new JsonObject { ["items"] = new JsonArray() });
        fixture.PublishEvent("BudgetCreated", correlationId, new JsonObject { ["budgetId"] = budgetId });
        await WaitForStateAsync(client, correlationId, "WaitingBudgetApproval");
        fixture.PublishEvent("BudgetApproved", correlationId, new JsonObject { ["budgetId"] = budgetId, ["simulatePaymentFailure"] = true });
        await WaitForStateAsync(client, correlationId, "WaitingPayment");
        fixture.PublishEvent("PaymentRejected", correlationId, new JsonObject { ["budgetId"] = budgetId, ["reason"] = "Recusado" });

        var cancelBudget = await probe.WaitForAsync("CancelBudgetCommand", correlationId);
        await probe.WaitForAsync("CancelServiceOrderCommand", correlationId);
        Assert.Equal(budgetId, cancelBudget.Payload!["budgetId"]!.GetValue<Guid>());
        await WaitForStateAsync(client, correlationId, "Failed");
    }

    [Fact]
    public async Task InvalidMessages_ShouldBeDiscardedWithoutStoppingTheConsumer()
    {
        await fixture.WaitForConsumerAsync();
        using var client = fixture.Factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString("D");

        fixture.PublishRaw("ServiceOrderOpened", Encoding.UTF8.GetBytes("não é json"));
        fixture.PublishRaw("ServiceOrderOpened", Encoding.UTF8.GetBytes("{\"type\":\"ServiceOrderOpened\"}"));
        fixture.PublishEvent("BudgetCreated", Guid.NewGuid().ToString("D"));
        fixture.PublishEvent("ServiceOrderOpened", correlationId);

        await WaitForStateAsync(client, correlationId, "WaitingBudget");
    }

    [Fact]
    public async Task Endpoints_ShouldExposeHealthAndNotFound()
    {
        await fixture.WaitForConsumerAsync();
        using var client = fixture.Factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await SagaFixture.WaitForAsync(async () =>
        {
            var response = await client.GetAsync("/health");
            return response.StatusCode == HttpStatusCode.OK ? response : null;
        });
        var missing = await client.GetAsync($"/api/sagas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static Task<SagaInstanceResponse> WaitForStateAsync(HttpClient client, string correlationId, string state)
        => SagaFixture.WaitForAsync(async () =>
        {
            var response = await client.GetAsync($"/api/sagas/{correlationId}");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            var saga = await response.Content.ReadFromJsonAsync<SagaInstanceResponse>();
            return saga?.CurrentState == state ? saga : null;
        });
}
