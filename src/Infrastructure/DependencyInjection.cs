using Application.SagaOrchestration.Abstractions;
using Infrastructure.HealthChecks;
using Infrastructure.Messaging.RabbitMq;
using Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddDbContext<SagaDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgreSql")));

        services.AddScoped<ISagaRepository, SagaRepository>();
        services.AddScoped<ISagaReadRepository, SagaReadRepository>();
        services.AddSingleton<ICommandDispatcher, RabbitMqCommandDispatcher>();
        services.AddHostedService<RabbitMqDomainEventConsumer>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgresql")
            .AddCheck<RabbitMqHealthCheck>("rabbitmq");

        return services;
    }
}
