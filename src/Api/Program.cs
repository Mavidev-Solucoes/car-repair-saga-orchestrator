using Api.Common;
using Application;
using Application.SagaOrchestration.Queries;
using Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapGet(
        "/api/sagas/{correlationId}",
        async Task<IResult> (string correlationId, ISender sender, CancellationToken cancellationToken) =>
        {
            var saga = await sender.Send(new GetSagaByCorrelationIdQuery(correlationId), cancellationToken);
            return saga is null ? Results.NotFound() : Results.Ok(saga);
        })
    .WithName("GetSagaByCorrelationId")
    .WithTags("Sagas")
    .WithOpenApi();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status200OK
    }
});

app.Run();
