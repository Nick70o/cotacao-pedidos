namespace CotacaoPedidos.Apresentacao;

/// <summary>
/// Textos dos ícones "?" da tela. Ficam todos aqui para facilitar a revisão da redação sem mexer no layout.
/// </summary>
internal static class TextosAjuda
{
    // ---- Aba Cotação ----

    public const string Filial =
        "Código da filial que fez o pedido de compra no ETrade.\n\n" +
        "Não sabe o código? Clique na lupa (ou tecle F2) para pesquisar pelo nome ou pela cidade. " +
        "O nome da filial aparece embaixo do campo para você conferir.";

    public const string Sequencia =
        "Número (sequência) do pedido de compra no ETrade. Junto com a Filial, identifica o pedido.\n\n" +
        "Tecle Enter para localizar.";

    public const string Localizar =
        "Consulta o pedido no ETrade e a planilha de cotação no Google e atualiza o Status.\n\n" +
        "Use antes de qualquer outra ação e sempre que o fornecedor avisar que terminou de preencher. " +
        "Os botões Exportar, Sobrescrever e Importar só ficam disponíveis quando fazem sentido para o " +
        "pedido localizado.";

    public const string Status =
        "Pedido não localizado: não existe pedido ativo nas operações 70/72 com essa Filial e Sequência.\n\n" +
        "Cotação Pendente: pedido na Operação 70 ainda sem planilha. Use Exportar.\n\n" +
        "Aguardando Cotação: planilha criada; o fornecedor ainda não preencheu nenhum preço.\n\n" +
        "Cotação Realizada: o fornecedor preencheu pelo menos um preço. Confira a planilha e use Importar.\n\n" +
        "Cotação Importada: preços gravados no ETrade e pedido na Operação 72. A planilha fica somente leitura.\n\n" +
        "Pedido Cancelado: o pedido foi desefetivado no ETrade.";

    public const string Exportar =
        "Cria a planilha de cotação no Google com os itens do pedido (Código, Código EAN, Nome e Quantidade).\n\n" +
        "O fornecedor só consegue preencher a coluna Preço; o resto fica bloqueado. " +
        "Disponível com o pedido na Operação 70. Se o pedido já tiver planilha, pergunta se você quer sobrescrever.";

    public const string Sobrescrever =
        "Arquiva a planilha atual (pasta \"Registros Arquivados\") e cria uma nova com os dados atuais do pedido.\n\n" +
        "Use quando o pedido foi alterado no ETrade depois da exportação. O link antigo deixa de funcionar: " +
        "envie o novo link ao fornecedor. Não é permitido depois da importação.";

    public const string Importar =
        "Grava os preços da planilha no ETrade e muda o pedido da Operação 70 para a 72 (Cotação Realizada).\n\n" +
        "Antes de gravar, mostra um resumo para você conferir. Produtos sem preço ficam com R$ 0,00. " +
        "Depois da importação a planilha fica somente leitura para o fornecedor.\n\n" +
        "Só disponível com status Cotação Realizada e com a planilha conferindo com o pedido.";

    public const string CopiarLink =
        "Copia o link da planilha para você colar no e-mail ou no WhatsApp do fornecedor.\n\n" +
        "Quem tem o link consegue preencher os preços, sem precisar de conta Google.";

    public const string AbrirPlanilha =
        "Abre a planilha no navegador para você conferir os preços preenchidos pelo fornecedor.";

    // ---- Aba Histórico ----

    public const string FiltroHistorico =
        "Mostra só as planilhas da Filial e/ou Sequência informadas. Deixe em branco para ver todas.";

    public const string MostrarArquivadas =
        "Inclui as planilhas substituídas pelo Sobrescrever (pasta \"Registros Arquivados\"). " +
        "Elas servem só para consulta e não podem mais ser importadas.";

    public const string AtualizarHistorico =
        "Busca novamente a lista de planilhas no Google Drive. A lista é carregada ao abrir a aba; " +
        "use Atualizar para ver o que mudou depois disso.";

    public const string AbrirPlanilhaHistorico =
        "Abre a planilha selecionada no navegador (o mesmo que dar duplo clique na linha).\n\n" +
        "As arquivadas só abrem no navegador logado na conta Google da aplicação, porque o link público " +
        "delas foi desativado.";

    public const string CopiarLinkHistorico =
        "Copia o link da planilha selecionada.";
}
