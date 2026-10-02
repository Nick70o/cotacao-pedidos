namespace CotacaoPedidos.Negocio;

/// <summary>Movimento (pedido) localizado no ETrade por Filial + Sequência.</summary>
public sealed record PedidoMovimento(
    Guid Ide,
    int Filial,
    int Sequencia,
    int Operacao,
    Guid OperacaoIde,
    bool Desefetivado,
    decimal TotalProdutos,
    decimal TotalFinal)
{
    public bool EstaNaOperacao70 => Operacao == ETradeConstantes.OperacaoPedidoCompra;
    public bool EstaNaOperacao72 => Operacao == ETradeConstantes.OperacaoCotacaoRealizada;
}

/// <summary>Linha exportada para a aba Cotação (Código, Código EAN, Nome, Quantidade).</summary>
public sealed record ItemPedido(string Codigo, string CodigoEan, string Nome, decimal Quantidade);

/// <summary>Preço a gravar no ETrade. Célula vazia deve chegar aqui já convertida para 0,00.</summary>
public sealed record PrecoItem(string Codigo, decimal NovoPreco);

/// <summary>Resultado de uma importação concluída (após COMMIT).</summary>
/// <param name="AvisoPlanilha">
/// Preenchido quando o COMMIT deu certo, mas a planilha não pôde ser marcada como importada (seção 17).
/// O próximo "Localizar pedido" conclui essa etapa.
/// </param>
public sealed record ResultadoImportacao(
    int ItensAtualizados, decimal TotalProdutos, decimal TotalFinal, string? AvisoPlanilha = null);

/// <summary>Filial cadastrada no ETrade (lupa da tela principal).</summary>
public sealed record Filial(int Codigo, string Nome, string Cidade, string Uf);

/// <summary>Conteúdo da aba Controle de uma planilha de cotação (seção 9.2).</summary>
public sealed record ControleCotacao(
    Guid MovimentoIde,
    int Filial,
    int Sequencia,
    string SpreadsheetId,
    string Status,
    DateTime? DataExportacao,
    DateTime? DataImportacao);

/// <summary>Uma planilha de cotação já criada e vinculada a um pedido.</summary>
public sealed record PlanilhaVinculada(string SpreadsheetId, string Nome, ControleCotacao Controle)
{
    public string Link => LinkPlanilha.Obter(SpreadsheetId);
}

/// <summary>Resultado do botão Exportar: a planilha criada, ou a que já existia (seção 19.1).</summary>
public sealed record ResultadoExportacao(PlanilhaVinculada Planilha, bool JaExistia);

/// <summary>Linha da aba Cotação como está na planilha no momento da leitura.</summary>
/// <param name="NumeroLinha">Número da linha na planilha (1 = cabeçalho), usado nas mensagens ao comprador.</param>
/// <param name="Quantidade">null quando a célula não contém um número.</param>
/// <param name="PrecoDigitado">Conteúdo da célula Preço; null quando vazia (seção 12: vazio é diferente de zero).</param>
/// <param name="Preco">Preço convertido; null quando a célula está vazia ou não contém um número válido.</param>
public sealed record LinhaCotacao(
    int NumeroLinha,
    string Codigo,
    string CodigoEan,
    string Nome,
    decimal? Quantidade,
    string? PrecoDigitado,
    decimal? Preco)
{
    public bool PrecoPreenchido => PrecoDigitado is not null;
    public bool PrecoInvalido => PrecoDigitado is not null && Preco is null;
}

/// <summary>Uma planilha de cotação (ativa ou arquivada), exibida na aba Histórico.</summary>
public sealed record RegistroHistorico(
    string SpreadsheetId,
    string Nome,
    string Status,
    int? Filial,
    int? Sequencia,
    bool Arquivada,
    DateTime? CriadaEm,
    DateTime? AlteradaEm)
{
    public string Link => LinkPlanilha.Obter(SpreadsheetId);

    /// <summary>
    /// Cotação importada não pode ser excluída: a planilha é o registro dos preços gravados no ETrade e a trava
    /// contra importação em dobro (seção 19.2).
    /// </summary>
    public bool PodeExcluir => !string.Equals(Status, NomesStatus.CotacaoImportada, StringComparison.Ordinal);
}
