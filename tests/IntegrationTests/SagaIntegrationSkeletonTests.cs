namespace IntegrationTests;

public sealed class SagaIntegrationSkeletonTests
{
    [Fact(Skip = "Requires PostgreSQL and RabbitMQ containers to be started for end-to-end validation.")]
    public Task HealthEndpoint_ShouldReportHealthy_WhenDependenciesAreAvailable()
    {
        return Task.CompletedTask;
    }

    [Fact(Skip = "Requires RabbitMQ wiring plus participant services to validate the full orchestrated saga flow.")]
    public Task Workflow_ShouldPersistSagaStateAcrossPublishedEvents()
    {
        return Task.CompletedTask;
    }
}
