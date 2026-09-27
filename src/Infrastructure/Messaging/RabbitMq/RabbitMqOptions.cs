using Application.SagaOrchestration.Contracts;

namespace Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string VirtualHost { get; init; } = "/";
    public string DomainEventsExchange { get; init; } = "domain-events";
    public string DomainEventsQueue { get; init; } = "saga-orchestrator-events";
    public string CommandsExchange { get; init; } = "commands";
    public string[] DomainEventRoutingKeys { get; init; } = [.. SagaEventTypes.All];
}
