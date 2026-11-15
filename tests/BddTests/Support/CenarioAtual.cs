namespace BddTests.Support;

/// <summary>Dados de um cenário (injetado pelo Reqnroll a cada cenário e descartado no fim).</summary>
public sealed class CenarioAtual : IDisposable
{
    public string OrdemDeServico { get; } = Guid.NewGuid().ToString("D");
    public Guid Orcamento { get; } = Guid.NewGuid();
    public Guid Pagamento { get; } = Guid.NewGuid();
    public CommandsProbe Comandos { get; set; } = null!;
    public HttpClient Cliente { get; set; } = null!;

    public void Dispose()
    {
        Comandos?.Dispose();
        Cliente?.Dispose();
    }
}
