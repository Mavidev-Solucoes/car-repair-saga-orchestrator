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
