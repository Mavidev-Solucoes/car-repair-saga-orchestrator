using System.Text.Json;
using System.Text.Json.Nodes;
using Application.SagaOrchestration.Messages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace IntegrationTests;

/// <summary>
/// Sobe Postgres e RabbitMQ reais (Testcontainers) e a API da saga apontando para eles.
/// As migrations são aplicadas pelo próprio serviço na subida.
/// </summary>
public sealed class SagaFixture : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", _postgres.GetConnectionString());
            builder.UseSetting("RabbitMq:HostName", _rabbit.Hostname);
            builder.UseSetting("RabbitMq:Port", _rabbit.GetMappedPublicPort(5672).ToString());
        });

        _ = Factory.Services;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public IConnection CreateRawConnection() => new ConnectionFactory
    {
        HostName = _rabbit.Hostname,
        Port = _rabbit.GetMappedPublicPort(5672),
        UserName = "guest",
        Password = "guest"
    }.CreateConnection();

    public void PublishEvent(string type, string correlationId, JsonObject? payload = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new IntegrationMessage { Type = type, CorrelationId = correlationId, Payload = payload ?? new JsonObject() },
            JsonOptions);
        PublishRaw(type, body);
    }

    public void PublishRaw(string routingKey, byte[] body)
    {
        using var connection = CreateRawConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare("car-repair.events", ExchangeType.Topic, durable: true, autoDelete: false);
        channel.BasicPublish("car-repair.events", routingKey, null, body);
    }

    public async Task WaitForConsumerAsync()
    {
        using var connection = CreateRawConnection();
        await WaitForAsync(() =>
        {
            try
            {
                using var channel = connection.CreateModel();
                return Task.FromResult(channel.QueueDeclarePassive("saga-orchestrator.events").ConsumerCount > 0 ? "ok" : null);
            }
            catch
            {
                return Task.FromResult<string?>(null);
            }
        }, TimeSpan.FromSeconds(30));
    }

    public static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var value = await probe();
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condição não foi atingida a tempo.");
    }
}

[CollectionDefinition(Name)]
public sealed class SagaCollection : ICollectionFixture<SagaFixture>
{
    public const string Name = "saga";
}

/// <summary>Fila temporária ligada a car-repair.commands para conferir os comandos que a saga enviou.</summary>
public sealed class CommandsProbe : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly string _queue;
    private readonly List<IntegrationMessage> _received = [];

    public CommandsProbe(SagaFixture fixture)
    {
        _connection = fixture.CreateRawConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare("car-repair.commands", ExchangeType.Topic, durable: true, autoDelete: false);
        _queue = _channel.QueueDeclare().QueueName;
        _channel.QueueBind(_queue, "car-repair.commands", "#");
    }

    public IReadOnlyList<IntegrationMessage> Received => _received;

    public Task<IntegrationMessage> WaitForAsync(string type, string correlationId) => SagaFixture.WaitForAsync(() =>
    {
        while (_channel.BasicGet(_queue, true) is { } delivery)
        {
            _received.Add(JsonSerializer.Deserialize<IntegrationMessage>(delivery.Body.Span, JsonOptions)!);
        }

        return Task.FromResult(_received.FirstOrDefault(message => message.Type == type && message.CorrelationId == correlationId));
    });

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
