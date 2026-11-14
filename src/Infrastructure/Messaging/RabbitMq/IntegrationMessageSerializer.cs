using System.Text.Json;

namespace Infrastructure.Messaging.RabbitMq;

/// <summary>
/// JSON em camelCase, aceitando qualquer caixa na leitura (docs/contrato-mensageria.md).
/// </summary>
public static class IntegrationMessageSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
