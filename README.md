# car-repair-saga-orchestrator

Microserviço responsável por coordenar transações distribuídas do fluxo de reparo de veículos usando o padrão **Orchestrated Saga**.

## Responsabilidades

- Consumir eventos de domínio no RabbitMQ.
- Persistir o estado da saga e histórico de transições no PostgreSQL.
- Publicar comandos para os serviços participantes.
- Executar fluxo de compensação quando houver falhas.

## Estados da Saga

- Started
- WaitingBudget
- WaitingBudgetApproval
- WaitingPayment
- WaitingProduction
- Completed
- Compensating
- Failed

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
