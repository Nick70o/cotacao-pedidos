# Changelog

Todas as mudanças relevantes do projeto. A versão atual aparece no rodapé do programa e é definida em
`Version`, em `CotacaoPedidos.Apresentacao/CotacaoPedidos.Apresentacao.csproj`.

## [1.1.2] - 2026-10-02

### Corrigido
- Aba Histórico: o cursor de carregamento ficava girando sobre a lista depois do Atualizar. Era um defeito do
  DataGridView do Windows Forms: se o mouse passasse pela divisa de uma coluna durante a espera, a grade guardava
  o cursor de espera como se fosse o dela. Agora ele volta ao normal ao terminar o carregamento.

### Alterado
- Rodapé: a build mostra só a data da compilação, sem a hora.

## [1.1.1] - 2026-10-02

### Adicionado
- **Excluir planilha** na aba Histórico (botão ou tecla Delete, sempre com confirmação). A planilha vai para a
  lixeira do Google Drive, de onde pode ser restaurada por 30 dias. Antes de excluir, o programa relê a planilha e,
  se for a ativa, consulta o pedido no ETrade. A planilha ativa é arquivada antes: o link do fornecedor para na
  hora e, se for restaurada, ela volta como arquivada. Cotações importadas (ou com o pedido já na Operação 72)
  não podem ser excluídas.

### Alterado
- **Ícone de ajuda "?" mais discreto**: em vez da bola azul preenchida, só o contorno em cinza; ao passar o
  mouse ele fica preenchido com a cor do logo. Também ficou um pouco menor e o "?" é desenhado sem as franjas
  coloridas do ClearType. Em alto contraste do Windows, usa as cores do sistema.

## [1.1.0] - 2026-10-01

### Adicionado
- **Logo e ícone do aplicativo**: o "C" de Cotação com a célula de preço confirmada. O ícone (vários tamanhos,
  de 16 a 256 px) aparece na janela, na barra de tarefas, no executável e no atalho da área de trabalho.
  O desenho de origem está em `CotacaoPedidos.Apresentacao/Recursos/icone.svg`.
- **README**: novo passo 4, "Criar o executável e o atalho na área de trabalho" (`dotnet publish`, atalho e
  como atualizar).

### Alterado
- `credentials.json` e `appsettings.json` locais passam a acompanhar o `dotnet publish`.

## [1.0.0] - 2026-10-01

Primeira versão publicada.

- Localizar pedido por Filial + Sequência (com lupa de filiais) e calcular o status da cotação.
- Exportar os itens do pedido para uma planilha do Google Sheets protegida (o fornecedor só edita o *Preço*).
- Sobrescrever: arquiva a planilha atual e gera uma nova com os dados atuais do pedido.
- Importar os preços para o ETrade em uma única transação, com resumo de conferência e validações
  (itens, preços, vínculo pedido × planilha).
- Aba Histórico com as planilhas ativas e arquivadas.
- Ajuda "?" em cada opção da tela; versão e data da compilação no rodapé.
- Arquitetura em 3 camadas (Apresentação, Negócio, Dados).
