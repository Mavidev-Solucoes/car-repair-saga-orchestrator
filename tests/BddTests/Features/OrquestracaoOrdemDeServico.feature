# language: pt-BR
Funcionalidade: Orquestração da ordem de serviço
  Como oficina
  Quero que a saga coordene orçamento, pagamento e execução entre os microsserviços
  Para que a ordem de serviço só seja concluída quando todas as etapas derem certo
  e seja compensada quando alguma delas falhar

  Contexto:
    Dado que o orquestrador está consumindo os eventos

  Cenário: Ordem de serviço concluída do início ao fim
    Dado que uma ordem de serviço foi aberta para "Maria Silva" com os itens:
      | descrição     | valor | quantidade |
      | Troca de óleo | 150   | 1          |
      | Filtro de ar  | 45.5  | 2          |
    Então a saga pede ao billing a criação do orçamento para "Maria Silva"
    Quando o orçamento de 241 é criado
    E o cliente aprova o orçamento
    Então a saga pede ao billing a cobrança do orçamento
    Quando o pagamento é aprovado
    Então a saga pede à produção a ordem de execução com 2 itens e o pagamento
    Quando a produção conclui o serviço
    Então a saga pede o fechamento da ordem de serviço
    E a saga termina no estado "Completed" com o histórico:
      | evento             | de                    | para                  |
      | ServiceOrderOpened | Started               | WaitingBudget         |
      | BudgetCreated      | WaitingBudget         | WaitingBudgetApproval |
      | BudgetApproved     | WaitingBudgetApproval | WaitingPayment        |
      | PaymentApproved    | WaitingPayment        | WaitingProduction     |
      | WorkCompleted      | WaitingProduction     | Completed             |

  Esquema do Cenário: Compensação quando <falha>
    Dado que uma ordem de serviço chegou à etapa "<etapa>"
    Quando chega o evento de falha "<evento>"
    Então a saga envia os comandos de compensação "<compensações>"
    E a saga não envia o comando "<não enviado>"
    E a saga termina no estado "Failed"

    Exemplos:
      | falha                      | etapa                 | evento          | compensações                                 | não enviado            |
      | o cliente recusa o orçamento | WaitingBudgetApproval | BudgetRejected  | CancelServiceOrderCommand                    | CancelBudgetCommand    |
      | o pagamento é recusado     | WaitingPayment        | PaymentRejected | CancelBudgetCommand, CancelServiceOrderCommand | CreateWorkOrderCommand |
      | a execução falha           | WaitingProduction     | WorkFailed      | CancelBudgetCommand, CancelServiceOrderCommand | CloseServiceOrderCommand |

  Cenário: Falha que chega depois da conclusão é ignorada
    Dado que uma ordem de serviço foi concluída
    Quando chega o evento de falha "WorkFailed"
    Então a saga continua no estado "Completed"
    E a saga não envia o comando "CancelBudgetCommand"
