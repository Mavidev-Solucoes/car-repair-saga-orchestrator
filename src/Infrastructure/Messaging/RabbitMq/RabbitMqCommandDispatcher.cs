using System.Text;
using System.Text.Json;
using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Messages;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqCommandDispatcher(IOptions<RabbitMqOptions> options) : ICommandDispatcher
{
    private readonly RabbitMqOptions _options = options.Value;

    public Task DispatchAsync(CommandMessage command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(_options.CommandsExchange, ExchangeType.Topic, durable: true, autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command));
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";

        channel.BasicPublish(
            exchange: _options.CommandsExchange,
            routingKey: command.CommandType,
            mandatory: false,
            basicProperties: properties,
            body: body);

        return Task.CompletedTask;
    }
}
