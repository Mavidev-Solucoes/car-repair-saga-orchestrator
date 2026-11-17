using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace IntegrationTests;

[Collection(SagaCollection.Name)]
public sealed class SwaggerTests(SagaFixture fixture)
{
    [Fact]
    public async Task Swagger_ShouldServeADocumentTheBundledSwaggerUiCanRender()
    {
        await using var enabled = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Swagger:Enabled", "true"));
        using var client = enabled.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // O Swagger UI do Swashbuckle 6.6.2 só renderiza openapi 3.0.0 a 3.0.3; 3.0.4 (Microsoft.OpenApi mais novo)
        // aparece como "does not specify a valid version field".
        Assert.Matches(@"^3\.0\.[0-3]$", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/sagas/{correlationId}", out _));
    }
}
