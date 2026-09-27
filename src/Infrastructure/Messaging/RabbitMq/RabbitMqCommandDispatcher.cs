using System.Text;
using System.Text.Json;
using Application.SagaOrchestration.Abstractions;
using Application.SagaOrchestration.Messages;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqCommandDispatcher : ICommandDispatcher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly object _channelLock = new();
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqCommandDispatcher(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
        _connection = CreateConnection(_options);
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_options.CommandsExchange, ExchangeType.Topic, durable: true, autoDelete: false);
    }

    public Task DispatchAsync(CommandMessage command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command.CommandType))
        {
            throw new ArgumentException("CommandType is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(command));
        }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command));

        lock (_channelLock)
        {
            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";

            _channel.BasicPublish(
                exchange: _options.CommandsExchange,
                routingKey: command.CommandType,
                mandatory: false,
                basicProperties: properties,
                body: body);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }

    private static IConnection CreateConnection(RabbitMqOptions options)
    {
        var factory = new ConnectionFactory
        {
            AutomaticRecoveryEnabled = true,
            HostName = options.HostName,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            TopologyRecoveryEnabled = true,
            VirtualHost = options.VirtualHost
        };

        return factory.CreateConnection();
    }
}
