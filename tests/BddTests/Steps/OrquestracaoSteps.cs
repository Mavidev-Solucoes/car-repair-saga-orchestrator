using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Application.SagaOrchestration.Messages;
using Application.SagaOrchestration.Queries;
using BddTests.Support;
using Reqnroll;

namespace BddTests.Steps;

/// <summary>
/// Passos da orquestração: publicam os eventos dos outros serviços no RabbitMQ, conferem os comandos
/// que a saga envia em car-repair.commands e consultam o estado pela API (/api/sagas/{id}).
/// </summary>
[Binding]
public sealed class OrquestracaoSteps(CenarioAtual cenario)
{
    private static readonly string[] EtapasEmOrdem =
        ["WaitingBudget", "WaitingBudgetApproval", "WaitingPayment", "WaitingProduction", "Completed"];

    private static SagaEnvironment Ambiente => SagaEnvironment.Current;

    [Given("que o orquestrador está consumindo os eventos")]
    public async Task DadoQueOOrquestradorEstaConsumindo()
    {
        await Ambiente.WaitForConsumerAsync();
        cenario.Comandos = new CommandsProbe(Ambiente);
        cenario.Cliente = Ambiente.Factory.CreateClient();
    }

    [Given("que uma ordem de serviço foi aberta para {string} com os itens:")]
    public async Task DadoQueUmaOrdemFoiAberta(string cliente, DataTable itens)
    {
        var lista = new JsonArray();
        foreach (var linha in itens.Rows)
        {
            lista.Add(new JsonObject
            {
                ["description"] = linha["descrição"],
                ["unitPrice"] = decimal.Parse(linha["valor"], CultureInfo.InvariantCulture),
                ["quantity"] = int.Parse(linha["quantidade"], CultureInfo.InvariantCulture)
            });
        }

        Publicar("ServiceOrderOpened", new JsonObject
        {
            ["serviceOrderId"] = cenario.OrdemDeServico,
            ["customerName"] = cliente,
            ["items"] = lista
        });
        await AguardarEstadoAsync("WaitingBudget");
    }

    [Given("que uma ordem de serviço chegou à etapa {string}")]
    public async Task DadoQueUmaOrdemChegouAEtapa(string etapa)
    {
        var indice = Array.IndexOf(EtapasEmOrdem, etapa);
        Assert.True(indice >= 0, $"Etapa desconhecida: {etapa}");

        Publicar("ServiceOrderOpened", new JsonObject
        {
            ["customerName"] = "Cliente BDD",
            ["items"] = new JsonArray(new JsonObject { ["description"] = "Revisão", ["unitPrice"] = 300m, ["quantity"] = 1 })
        });
        await AguardarEstadoAsync(EtapasEmOrdem[0]);

        for (var i = 1; i <= indice; i++)
        {
            await AvancarParaAsync(EtapasEmOrdem[i]);
        }
    }

    [Given("que uma ordem de serviço foi concluída")]
    public Task DadoQueUmaOrdemFoiConcluida() => DadoQueUmaOrdemChegouAEtapa("Completed");

    [When("o orçamento de {decimal} é criado")]
    public Task QuandoOOrcamentoECriado(decimal total)
    {
        Publicar("BudgetCreated", new JsonObject { ["budgetId"] = cenario.Orcamento, ["totalAmount"] = total });
        return AguardarEstadoAsync("WaitingBudgetApproval");
    }

    [When("o cliente aprova o orçamento")]
    public void QuandoOClienteAprovaOOrcamento() =>
        Publicar("BudgetApproved", new JsonObject { ["budgetId"] = cenario.Orcamento, ["simulatePaymentFailure"] = false });

    [When("o pagamento é aprovado")]
    public void QuandoOPagamentoEAprovado() =>
        Publicar("PaymentApproved", new JsonObject { ["paymentId"] = cenario.Pagamento, ["budgetId"] = cenario.Orcamento });

    [When("a produção conclui o serviço")]
    public void QuandoAProducaoConcluiOServico() =>
        Publicar("WorkCompleted", new JsonObject { ["workOrderId"] = Guid.NewGuid() });

    [When("chega o evento de falha {string}")]
    public void QuandoChegaOEventoDeFalha(string evento) =>
        Publicar(evento, new JsonObject { ["budgetId"] = cenario.Orcamento, ["reason"] = "Falha simulada no BDD" });

    [Then("a saga pede ao billing a criação do orçamento para {string}")]
    public async Task EntaoASagaPedeOOrcamento(string cliente)
    {
        var comando = await AguardarComandoAsync("CreateBudgetCommand");
        Assert.Equal(cliente, comando.Payload!["customerName"]!.GetValue<string>());
        Assert.Equal(2, comando.Payload["items"]!.AsArray().Count);
    }

    [Then("a saga pede ao billing a cobrança do orçamento")]
    public async Task EntaoASagaPedeACobranca()
    {
        var comando = await AguardarComandoAsync("ProcessPaymentCommand");
        Assert.Equal(cenario.Orcamento, comando.Payload!["budgetId"]!.GetValue<Guid>());
        Assert.Equal(241m, comando.Payload["totalAmount"]!.GetValue<decimal>());
    }

    [Then("a saga pede à produção a ordem de execução com {int} itens e o pagamento")]
    public async Task EntaoASagaPedeAOrdemDeExecucao(int quantidade)
    {
        var comando = await AguardarComandoAsync("CreateWorkOrderCommand");
        Assert.Equal(quantidade, comando.Payload!["items"]!.AsArray().Count);
        Assert.Equal(cenario.Pagamento, comando.Payload["paymentId"]!.GetValue<Guid>());
        Assert.Equal("Maria Silva", comando.Payload["customerName"]!.GetValue<string>());
    }

    [Then("a saga pede o fechamento da ordem de serviço")]
    public Task EntaoASagaPedeOFechamento() => AguardarComandoAsync("CloseServiceOrderCommand");

    [Then("a saga envia os comandos de compensação {string}")]
    public async Task EntaoASagaEnviaACompensacao(string comandos)
    {
        foreach (var comando in comandos.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var mensagem = await AguardarComandoAsync(comando);
            if (comando == "CancelBudgetCommand")
            {
                Assert.Equal(cenario.Orcamento, mensagem.Payload!["budgetId"]!.GetValue<Guid>());
            }
        }
    }

    [Then("a saga não envia o comando {string}")]
    public async Task EntaoASagaNaoEnviaOComando(string comando)
    {
        await AguardarFilaProcessadaAsync();
        Assert.DoesNotContain(cenario.Comandos.ReceivedFor(cenario.OrdemDeServico), mensagem => mensagem.Type == comando);
    }

    [Then("a saga termina no estado {string}")]
    public Task EntaoASagaTerminaNoEstado(string estado) => AguardarEstadoAsync(estado);

    [Then("a saga continua no estado {string}")]
    public async Task EntaoASagaContinuaNoEstado(string estado)
    {
        await AguardarFilaProcessadaAsync();
        var saga = await AguardarAsync(_ => true);
        Assert.Equal(estado, saga.CurrentState);
    }

    [Then("a saga termina no estado {string} com o histórico:")]
    public async Task EntaoASagaTerminaComHistorico(string estado, DataTable historico)
    {
        var saga = await AguardarEstadoAsync(estado);
        var esperado = historico.Rows.Select(linha => (linha["evento"], linha["de"], linha["para"])).ToList();
        var obtido = saga.History.Select(item => (item.Trigger, item.FromState, item.ToState)).ToList();

        Assert.Equal(esperado, obtido);
        Assert.Contains("Maria Silva", saga.Context);
    }

    private async Task AvancarParaAsync(string etapa)
    {
        switch (etapa)
        {
            case "WaitingBudgetApproval":
                Publicar("BudgetCreated", new JsonObject { ["budgetId"] = cenario.Orcamento, ["totalAmount"] = 300m });
                break;
            case "WaitingPayment":
                QuandoOClienteAprovaOOrcamento();
                break;
            case "WaitingProduction":
                QuandoOPagamentoEAprovado();
                break;
            case "Completed":
                QuandoAProducaoConcluiOServico();
                break;
        }

        await AguardarEstadoAsync(etapa);
    }

    private void Publicar(string evento, JsonObject payload) =>
        Ambiente.PublishEvent(evento, cenario.OrdemDeServico, payload);

    private Task<IntegrationMessage> AguardarComandoAsync(string comando) =>
        cenario.Comandos.WaitForAsync(comando, cenario.OrdemDeServico);

    /// <summary>
    /// O consumidor da saga processa um evento por vez (prefetch 1), na ordem da fila. Abrir uma ordem de serviço
    /// sentinela e esperar por ela garante que tudo o que foi publicado antes já foi tratado.
    /// </summary>
    private async Task AguardarFilaProcessadaAsync()
    {
        var sentinela = Guid.NewGuid().ToString("D");
        Ambiente.PublishEvent("ServiceOrderOpened", sentinela, new JsonObject { ["items"] = new JsonArray() });
        await AguardarAsync(saga => saga.CurrentState == "WaitingBudget", sentinela);
    }

    private Task<SagaInstanceResponse> AguardarEstadoAsync(string estado) =>
        AguardarAsync(saga => saga.CurrentState == estado);

    private Task<SagaInstanceResponse> AguardarAsync(Func<SagaInstanceResponse, bool> condicao, string? ordemDeServico = null) =>
        SagaEnvironment.WaitForAsync(async () =>
        {
            var resposta = await cenario.Cliente.GetAsync($"/api/sagas/{ordemDeServico ?? cenario.OrdemDeServico}");
            if (resposta.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            var saga = await resposta.Content.ReadFromJsonAsync<SagaInstanceResponse>();
            return saga is not null && condicao(saga) ? saga : null;
        });
}
