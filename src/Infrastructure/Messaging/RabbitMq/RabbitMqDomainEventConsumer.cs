using System.Text;
using System.Text.Json;
using Application.SagaOrchestration.Commands;
using Application.SagaOrchestration.Messages;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqDomainEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqDomainEventConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private CancellationToken _stoppingToken;
    private IConnection? _connection;
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                StartConsumer();
                await WaitUntilStoppedAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ connection is unavailable. Retrying in 5 seconds.");
                DisposeChannelAndConnection();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private void StartConsumer()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_options.DomainEventsExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.QueueDeclare(_options.DomainEventsQueue, durable: true, exclusive: false, autoDelete: false);
        foreach (var routingKey in _options.DomainEventRoutingKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _channel.QueueBind(_options.DomainEventsQueue, _options.DomainEventsExchange, routingKey);
        }
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleReceivedAsync;

        _channel.BasicConsume(
            queue: _options.DomainEventsQueue,
            autoAck: false,
            consumer: consumer);

    }

    private async Task HandleReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            var domainEvent = JsonSerializer.Deserialize<DomainEventMessage>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (domainEvent is null)
            {
                _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ProcessSagaEventCommand(domainEvent), _stoppingToken);

            _channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error processing domain event message.");
            _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public override void Dispose()
    {
        DisposeChannelAndConnection();
        base.Dispose();
    }

    private void DisposeChannelAndConnection()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _channel = null;
        _connection = null;
    }

    private static async Task WaitUntilStoppedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
