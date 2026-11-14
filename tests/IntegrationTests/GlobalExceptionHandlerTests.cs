using System.Text.Json;
using Api.Common;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntegrationTests;

[Collection(SagaCollection.Name)]
public sealed class GlobalExceptionHandlerTests(SagaFixture fixture)
{
    [Fact]
    public async Task ValidationException_ShouldReturnBadRequestWithErrors()
    {
        var (context, handled) = await HandleAsync(
            new ValidationException([new ValidationFailure("CorrelationId", "CorrelationId is required.")]),
            fixture.Factory.Services.GetRequiredService<IProblemDetailsService>());

        using var body = await ReadBodyAsync(context);
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("CorrelationId is required.", body.RootElement.GetProperty("errors").GetProperty("CorrelationId")[0].GetString());
    }

    [Fact]
    public async Task UnexpectedException_ShouldReturnInternalServerError()
    {
        var (context, _) = await HandleAsync(
            new InvalidOperationException("boom"),
            fixture.Factory.Services.GetRequiredService<IProblemDetailsService>());

        using var body = await ReadBodyAsync(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task WhenProblemDetailsCannotBeWritten_ShouldFallBackToPlainText()
    {
        var (context, handled) = await HandleAsync(new InvalidOperationException("boom"), new RefusingProblemDetailsService());

        context.Response.Body.Position = 0;
        Assert.True(handled);
        Assert.Equal("An unexpected error occurred.", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private async Task<(DefaultHttpContext Context, bool Handled)> HandleAsync(Exception exception, IProblemDetailsService problemDetailsService)
    {
        var context = new DefaultHttpContext { RequestServices = fixture.Factory.Services };
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, problemDetailsService);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        return (context, handled);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class RefusingProblemDetailsService : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context) => ValueTask.FromResult(false);
    }
}
