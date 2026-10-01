namespace CotacaoPedidos.Negocio;

public enum StatusCotacao
{
    PedidoNaoLocalizado,
    CotacaoPendente,
    AguardandoCotacao,
    CotacaoRealizada,
    CotacaoImportada,
    PedidoCancelado
}

/// <summary>
/// Textos oficiais dos status. Os três aqui presentes também nomeiam o arquivo da planilha
/// (seção 10) e ficam gravados na aba Controle - por isso são constantes, não apenas rótulos de tela.
/// </summary>
public static class NomesStatus
{
    public const string AguardandoCotacao = "Aguardando Cotação";
    public const string CotacaoRealizada = "Cotação Realizada";
    public const string CotacaoImportada = "Cotação Importada";
}

public static class StatusCotacaoExtensions
{
    public static string ParaTexto(this StatusCotacao status) => status switch
    {
        StatusCotacao.PedidoNaoLocalizado => "Pedido não localizado",
        StatusCotacao.CotacaoPendente => "Cotação Pendente",
        StatusCotacao.AguardandoCotacao => NomesStatus.AguardandoCotacao,
        StatusCotacao.CotacaoRealizada => NomesStatus.CotacaoRealizada,
        StatusCotacao.CotacaoImportada => NomesStatus.CotacaoImportada,
        StatusCotacao.PedidoCancelado => "Pedido Cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}

/// <summary>
/// Situação da planilha vinculada ao pedido (lida do Google Sheets).
/// Só existe quando há planilha; sem planilha, passe <c>null</c> para a calculadora.
/// </summary>
/// <param name="TemAlgumPrecoPreenchido">
/// Pelo menos uma célula da coluna Preço tem valor. O valor 0 digitado conta como preenchido;
/// célula vazia não conta (seção 6.4).
/// </param>
/// <param name="ControleIndicaImportada">A aba Controle está com Status = Cotação Importada.</param>
public sealed record EstadoPlanilha(bool TemAlgumPrecoPreenchido, bool ControleIndicaImportada);

/// <summary>Regras de status da cotação (seções 6 e 7 da especificação).</summary>
public static class CalculadoraStatus
{
    public static StatusCotacao Calcular(PedidoMovimento? pedido, EstadoPlanilha? planilha)
    {
        if (pedido is null)
            return StatusCotacao.PedidoNaoLocalizado;

        if (pedido.Desefetivado)
            return StatusCotacao.PedidoCancelado;

        if (pedido.EstaNaOperacao72)
        {
            // Seção 7: Operação 72 + aba Controle indicando importação concluída => Cotação Importada.
            // Operação 72 sem essa marca não faz parte do fluxo desta aplicação; tratamos como não localizado.
            // (Se o COMMIT deu certo mas a planilha não foi marcada, o orquestrador conclui essa etapa
            //  ao localizar o pedido - veja CotacaoOrchestrator.ManterPlanilhaCoerenteAsync.)
            return planilha is { ControleIndicaImportada: true }
                ? StatusCotacao.CotacaoImportada
                : StatusCotacao.PedidoNaoLocalizado;
        }

        // Operação 70
        if (planilha is null)
            return StatusCotacao.CotacaoPendente;

        return planilha.TemAlgumPrecoPreenchido
            ? StatusCotacao.CotacaoRealizada
            : StatusCotacao.AguardandoCotacao;
    }
}

/// <summary>
/// Retrato do pedido e da planilha no momento da consulta: o que a tela precisa para exibir o status e
/// habilitar os botões (seção 5.3). Cada ação volta a consultar o banco antes de executar - este objeto
/// não autoriza nada sozinho.
/// </summary>
/// <param name="Linhas">Linhas atuais da aba Cotação; vazio quando não há planilha.</param>
/// <param name="Divergencias">
/// Diferenças entre as colunas que não são Preço e os itens do pedido no ETrade. Qualquer divergência
/// bloqueia a importação.
/// </param>
public sealed record SituacaoCotacao(
    int Filial,
    int Sequencia,
    StatusCotacao Status,
    PedidoMovimento? Pedido,
    PlanilhaVinculada? Planilha,
    IReadOnlyList<LinhaCotacao> Linhas,
    IReadOnlyList<string> Divergencias)
{
    public bool TemPlanilha => Planilha is not null;

    private bool PedidoNaOperacao70 => Pedido is { EstaNaOperacao70: true, Desefetivado: false };

    /// <summary>Com planilha existente, Exportar oferece a sobrescrita (seção 19.1).</summary>
    public bool PodeExportar => PedidoNaOperacao70;

    public bool PodeSobrescrever =>
        PedidoNaOperacao70 && Status is StatusCotacao.AguardandoCotacao or StatusCotacao.CotacaoRealizada;

    public bool PodeImportar => Status == StatusCotacao.CotacaoRealizada && Divergencias.Count == 0;
}
