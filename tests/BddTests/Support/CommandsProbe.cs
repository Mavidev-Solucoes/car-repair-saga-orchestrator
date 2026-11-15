using System.Text.Json;
using Application.SagaOrchestration.Messages;
using RabbitMQ.Client;

namespace BddTests.Support;

/// <summary>Fila temporária ligada a car-repair.commands para conferir os comandos que a saga enviou.</summary>
public sealed class CommandsProbe : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly string _queue;
    private readonly List<IntegrationMessage> _received = [];

    public CommandsProbe(SagaEnvironment environment)
    {
        _connection = environment.CreateRawConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare("car-repair.commands", ExchangeType.Topic, durable: true, autoDelete: false);
        _queue = _channel.QueueDeclare().QueueName;
        _channel.QueueBind(_queue, "car-repair.commands", "#");
    }

    /// <summary>Comandos recebidos até agora para a ordem de serviço.</summary>
    public IReadOnlyList<IntegrationMessage> ReceivedFor(string correlationId)
    {
        Drain();
        return _received.Where(message => message.CorrelationId == correlationId).ToList();
    }

    public Task<IntegrationMessage> WaitForAsync(string type, string correlationId) => SagaEnvironment.WaitForAsync(() =>
        Task.FromResult(ReceivedFor(correlationId).FirstOrDefault(message => message.Type == type)));

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }

    private void Drain()
    {
        while (_channel.BasicGet(_queue, true) is { } delivery)
        {
            _received.Add(JsonSerializer.Deserialize<IntegrationMessage>(delivery.Body.Span, JsonOptions)!);
        }
    }
}
