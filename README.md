# car-repair-saga-orchestrator

Microserviço responsável por coordenar transações distribuídas do fluxo de reparo de veículos usando o padrão **Orchestrated Saga** com **Clean Architecture**, **CQRS**, **PostgreSQL** e **RabbitMQ**.

O orquestrador não implementa regras de negócio dos serviços participantes. Ele apenas coordena o fluxo por meio de eventos, transições de estado e publicação de comandos.

## Responsabilidades

- Consumir eventos de domínio no RabbitMQ.
- Persistir o estado da saga e o histórico de transições no PostgreSQL.
- Publicar comandos para os serviços participantes.
- Expor consulta de leitura para inspeção do estado atual da saga.
- Executar compensações quando houver falhas no fluxo.

## Estados da Saga

- Started
- WaitingBudget
- WaitingBudgetApproval
- WaitingPayment
- WaitingProduction
- Completed
- Compensating
- Failed

## Eventos Consumidos

- ServiceOrderOpened
- BudgetCreated
- BudgetApproved
- BudgetRejected
- PaymentApproved
- PaymentRejected
- WorkCompleted
- WorkFailed

## Comandos Publicados

- CreateBudgetCommand
- ProcessPaymentCommand
- CreateWorkOrderCommand
- CloseServiceOrderCommand
- CancelServiceOrderCommand
- CancelBudgetCommand

## Fluxos de Compensação

- `BudgetRejected` → `CancelServiceOrderCommand`
- `PaymentRejected` → `CancelBudgetCommand` → `CancelServiceOrderCommand`
- `WorkFailed` → `CancelBudgetCommand` → `CancelServiceOrderCommand`

## Persistência

As entidades persistidas são:

- `SagaInstance`
- `SagaHistory`

A migration inicial está em `src/Infrastructure/Persistence/Migrations/` com o nome `InitialCreate`.

## API

- `GET /api/sagas/{correlationId}`: consulta o estado atual e o histórico da saga.
- `GET /health`: readiness check com dependências reais (PostgreSQL e RabbitMQ).
- `GET /health/live`: liveness check do processo para cenários de container/startup.
- Swagger habilitado em ambiente de desenvolvimento.

## Executando localmente

### Via Docker Compose

```bash
docker compose up --build
```

Serviços expostos:

- API: `http://localhost:8080`
- RabbitMQ Management: `http://localhost:15672`

### Via .NET CLI

```bash
dotnet restore CarRepairSagaOrchestrator.sln
dotnet build CarRepairSagaOrchestrator.sln
dotnet test CarRepairSagaOrchestrator.sln
```

## Testes

- `tests/UnitTests`: testes unitários da coordenação da saga.
- `tests/IntegrationTests`: API e mensageria com Postgres e RabbitMQ reais (Testcontainers).
- `tests/BddTests`: BDD com [Reqnroll](https://reqnroll.net) (Gherkin em português) sobre o mesmo ambiente real.

### BDD

A feature [`OrquestracaoOrdemDeServico.feature`](tests/BddTests/Features/OrquestracaoOrdemDeServico.feature) descreve o fluxo completo da ordem de serviço do ponto de vista da saga:

- **fluxo feliz**: OS aberta → orçamento criado e aprovado → pagamento aprovado → execução concluída → OS fechada, conferindo cada comando enviado e o histórico de transições;
- **compensações** (esquema de cenário): orçamento recusado, pagamento recusado e falha na execução, com os comandos de compensação esperados e os que não podem sair;
- **falha tardia**: uma falha que chega depois da conclusão não reabre nem compensa a saga.

Os passos publicam os eventos dos outros serviços em `car-repair.events`, leem os comandos em `car-repair.commands` por uma fila temporária e consultam o estado em `GET /api/sagas/{correlationId}`. Rodam junto com os demais no `dotnet test` (precisa de Docker):

```bash
dotnet test tests/BddTests --logger "console;verbosity=detailed"
```

## CI (GitHub Actions)

O pipeline de CI está definido em `.github/workflows/ci.yml`.

Secrets obrigatórios para análise SonarCloud:

- `SONAR_TOKEN`
- `SONAR_PROJECT_KEY`
- `SONAR_ORGANIZATION`

## CD (GitHub Actions)

O pipeline de CD está definido em `.github/workflows/cd.yml` e roda somente após sucesso do workflow de CI na branch `main`.

Imagem publicada no GitHub Container Registry (`ghcr.io/${owner}/${repo}`) com as tags:

- `latest`
- `<commit-sha>`

Secrets obrigatórios para CD:

- Nenhum secret adicional. O workflow usa `GITHUB_TOKEN` automático do GitHub Actions com permissão `packages: write`.

## Observabilidade (New Relic)

A imagem traz o agente .NET do New Relic com o profiler desligado; no Kubernetes o ConfigMap liga o agente (`NEW_RELIC_APP_NAME=car-repair-saga-orchestrator`) e a license vem do Secret opcional `newrelic-license`.

O trace distribuído atravessa o RabbitMQ: `MessageTracing` grava `traceparent`/`tracestate` (W3C) e os headers do New Relic em cada mensagem publicada e, no consumo, abre a transação `RabbitMQ/<tipo>` que continua o trace de quem publicou, com o atributo `correlationId` (id da OS). Detalhes e consultas NRQL no README do `car-repair-k8s-infra`, seção "Observabilidade: New Relic APM e trace distribuído".

## Evidências de testes e cobertura — FIAP Fase 4

- **Cobertura de linhas:** **93,59%**, acima dos **80%** exigidos, na execução de CI da branch `main` de **15/11/2026**.
- **Evidência:** [GitHub Actions CI — testes, cobertura, SonarCloud e validação Docker aprovados](https://github.com/Mavidev-Solucoes/car-repair-saga-orchestrator/actions/runs/43690157854).
- **Testes executados nessa evidência:** 21 testes unitários, 12 testes de integração e 5 cenários BDD aprovados.
- **BDD em Gherkin/Reqnroll:** [`OrquestracaoOrdemDeServico.feature`](tests/BddTests/Features/OrquestracaoOrdemDeServico.feature), incluindo fluxo completo, recusas e compensações.
- **E2E com os quatro microsserviços:** [workflow Kubernetes (kind) aprovado em 16/11/2026](https://github.com/Mavidev-Solucoes/car-repair-k8s-infra/actions/runs/43935058397), confirmando os estados finais nos cenários de sucesso, pagamento recusado e falha na execução.

O workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) exige cobertura mínima de 80%. O percentual documentado corresponde à execução vinculada e pode variar em novos commits.

## Justificativa da estratégia de Saga

Foi escolhida a **orquestração centralizada** para concentrar a decisão das transições e dos comandos de compensação no `car-repair-saga-orchestrator`, mantendo as regras de negócio e a persistência nos respectivos serviços. A estratégia torna explícito o fluxo de abertura da OS, aprovação do orçamento, pagamento, execução e fechamento, bem como os caminhos de cancelamento diante de recusas ou falhas. O estado e o histórico são persistidos no PostgreSQL do orquestrador, e os serviços se comunicam por eventos e comandos RabbitMQ em vez de acessar bancos de outros serviços.

O [contrato de mensageria](docs/contrato-mensageria.md) detalha exchanges, eventos, comandos, `correlationId` e compensações. No fluxo validado pelo E2E, a falha de pagamento cancela orçamento/OS e a falha na execução estorna o pagamento e cancela orçamento/OS. O estado `Failed` indica que o orquestrador disparou a compensação; o teste E2E confirma separadamente os estados finais dos serviços participantes.

## Artefatos de entrega

- [Dockerfile](src/Api/Dockerfile), [manifests Kubernetes](k8s/base) e [pipelines CI/CD](.github/workflows).
- Swagger/OpenAPI para consulta à Saga em ambiente de desenvolvimento.
- [Infraestrutura Kubernetes e roteiro de demonstração](https://github.com/Mavidev-Solucoes/car-repair-k8s-infra).

