using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Infrastructure.Messaging.RabbitMq;

namespace Infrastructure.HealthChecks;

public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = CreateConnection(options.Value);
            using var channel = connection.CreateModel();
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ is reachable."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", exception));
        }
    }

    private static IConnection CreateConnection(RabbitMqOptions options)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost
        };

        return factory.CreateConnection();
    }
}
