using System.Text.Json;
using System.Text.Json.Nodes;
using Application.SagaOrchestration.Messages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using Reqnroll;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace BddTests.Support;

/// <summary>
/// Ambiente dos cenários: Postgres e RabbitMQ reais (Testcontainers) e a API da saga apontando para eles.
/// Sobe uma vez por execução; cada cenário usa uma ordem de serviço (correlationId) própria.
/// </summary>
[Binding]
public sealed class SagaEnvironment
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public static SagaEnvironment Current { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    [BeforeTestRun]
    public static async Task StartAsync()
    {
        var environment = new SagaEnvironment();
        await Task.WhenAll(environment._postgres.StartAsync(), environment._rabbit.StartAsync());

        environment.Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", environment._postgres.GetConnectionString());
            builder.UseSetting("RabbitMq:HostName", environment._rabbit.Hostname);
            builder.UseSetting("RabbitMq:Port", environment._rabbit.GetMappedPublicPort(5672).ToString());
        });

        _ = environment.Factory.Services;
        Current = environment;
    }

    [AfterTestRun]
    public static async Task StopAsync()
    {
        if (Current is null)
        {
            return;
        }

        await Current.Factory.DisposeAsync();
        await Current._postgres.DisposeAsync();
        await Current._rabbit.DisposeAsync();
    }

    public IConnection CreateRawConnection() => new ConnectionFactory
    {
        HostName = _rabbit.Hostname,
        Port = _rabbit.GetMappedPublicPort(5672),
        UserName = "guest",
        Password = "guest"
    }.CreateConnection();

    public void PublishEvent(string type, string correlationId, JsonObject payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new IntegrationMessage { Type = type, CorrelationId = correlationId, Payload = payload },
            JsonOptions);

        using var connection = CreateRawConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare("car-repair.events", ExchangeType.Topic, durable: true, autoDelete: false);
        channel.BasicPublish("car-repair.events", type, null, body);
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
