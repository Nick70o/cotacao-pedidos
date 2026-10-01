namespace CotacaoPedidos.Negocio;

/// <summary>
/// Confere pedido x planilha antes de qualquer importação (seções 13 e 18).
/// Não valida se os produtos e preços da planilha correspondem ao pedido - isso é feito
/// à parte, em <see cref="ConferenciaCotacao"/>.
/// </summary>
public static class ValidacaoVinculo
{
    public static void Validar(PedidoMovimento pedido, ControleCotacao controle, string spreadsheetIdUtilizado)
    {
        if (controle.MovimentoIde != pedido.Ide)
            throw new OperacaoBloqueadaException(
                "A planilha não pertence a este pedido (Movimento_Ide não confere).");

        if (controle.Filial != pedido.Filial)
            throw new OperacaoBloqueadaException("A planilha não corresponde à Filial informada.");

        if (controle.Sequencia != pedido.Sequencia)
            throw new OperacaoBloqueadaException("A planilha não corresponde à Sequência informada.");

        if (!string.Equals(controle.SpreadsheetId, spreadsheetIdUtilizado, StringComparison.Ordinal))
            throw new OperacaoBloqueadaException(
                "O SpreadsheetId registrado na planilha não corresponde à planilha utilizada.");

        if (!pedido.EstaNaOperacao70)
            throw new OperacaoBloqueadaException(
                "O pedido não está mais na Operação 70 - Pedido de Compra.");

        if (string.Equals(controle.Status, NomesStatus.CotacaoImportada, StringComparison.Ordinal))
            throw new OperacaoBloqueadaException("Esta cotação já foi importada anteriormente.");
    }
}

/// <summary>Nome do arquivo da planilha: "[Status] - Filial [Código] - Pedido [Sequência]" (seção 10).</summary>
public static class NomeArquivoCotacao
{
    public static string Gerar(string statusTexto, int filial, int sequencia) =>
        $"{statusTexto} - Filial {filial} - Pedido {sequencia}";

    /// <summary>
    /// Gera o nome esperado para os três status que possuem planilha.
    /// Cotação Pendente, Pedido não localizado e Pedido Cancelado não têm arquivo (seção 10).
    /// </summary>
    public static string ParaStatus(StatusCotacao status, int filial, int sequencia) => status switch
    {
        StatusCotacao.AguardandoCotacao => Gerar(NomesStatus.AguardandoCotacao, filial, sequencia),
        StatusCotacao.CotacaoRealizada => Gerar(NomesStatus.CotacaoRealizada, filial, sequencia),
        StatusCotacao.CotacaoImportada => Gerar(NomesStatus.CotacaoImportada, filial, sequencia),
        _ => throw new InvalidOperationException(
            $"O status '{status}' não possui planilha e portanto não tem nome de arquivo.")
    };

    /// <summary>Os três nomes possíveis para um pedido, usados para localizar a planilha ativa pelo nome.</summary>
    public static IReadOnlyList<string> TodosOsNomesPossiveis(int filial, int sequencia) =>
    [
        Gerar(NomesStatus.AguardandoCotacao, filial, sequencia),
        Gerar(NomesStatus.CotacaoRealizada, filial, sequencia),
        Gerar(NomesStatus.CotacaoImportada, filial, sequencia)
    ];
}

/// <summary>Link de acesso de uma planilha do Google Sheets.</summary>
public static class LinkPlanilha
{
    public static string Obter(string spreadsheetId) =>
        $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit";
}
