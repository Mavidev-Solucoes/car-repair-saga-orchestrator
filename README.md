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
- CompensateWorkOrderCommand
- ReturnServiceOrderToApprovedCommand

## Fluxos de Compensação

- `BudgetRejected` → `CancelServiceOrderCommand`
- `PaymentRejected` → `CancelBudgetCommand` → `CancelServiceOrderCommand`
- `WorkFailed` → `CompensateWorkOrderCommand` → `ReturnServiceOrderToApprovedCommand`

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
- `tests/IntegrationTests`: esqueletos de testes de integração para cenários com infraestrutura real.

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
