# Changelog

Todas as mudanças relevantes do projeto. A versão atual aparece no rodapé do programa e é definida em
`Version`, em `CotacaoPedidos.Apresentacao/CotacaoPedidos.Apresentacao.csproj`.

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
