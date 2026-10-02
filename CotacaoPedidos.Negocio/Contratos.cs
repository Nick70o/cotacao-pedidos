namespace CotacaoPedidos.Negocio;

// Contratos que a camada de Dados implementa. A camada de Negócio depende só destas interfaces - nunca de
// SQL Server ou do Google diretamente -, o que permite testar as regras sem banco nem internet.

/// <summary>Banco ETrade (SQL Server).</summary>
public interface IETradeRepository
{
    /// <summary>Movimento ativo nas operações 70/72 para Filial + Sequência; null se não existir.</summary>
    Task<PedidoMovimento?> LocalizarPedidoAsync(int filial, int sequencia, CancellationToken ct = default);

    Task<IReadOnlyList<Filial>> ListarFiliaisAsync(CancellationToken ct = default);

    /// <summary>Itens do pedido na Operação 70, ordenados por nome (seção 7).</summary>
    Task<IReadOnlyList<ItemPedido>> ObterItensParaCotacaoAsync(PedidoMovimento pedido, CancellationToken ct = default);

    /// <summary>
    /// Grava os preços, recalcula os totais e muda a operação 70 -> 72 numa única transação (seção 16).
    /// Os preços chegam já validados pela camada de Negócio; qualquer falha desfaz tudo (ROLLBACK).
    /// </summary>
    Task<ResultadoImportacao> ImportarPrecosAsync(
        PedidoMovimento pedido, IReadOnlyCollection<PrecoItem> precos, CancellationToken ct = default);
}

/// <summary>Planilhas de cotação (Google Sheets/Drive).</summary>
public interface IPlanilhaRepository
{
    /// <summary>A planilha ativa do pedido, confirmada pela aba Controle; null se não existir.</summary>
    Task<PlanilhaVinculada?> LocalizarPlanilhaAtivaAsync(PedidoMovimento pedido, CancellationToken ct = default);

    /// <summary>Cria a planilha com abas Cotação e Controle, proteções e link de edição (seções 8 e 9).</summary>
    Task<PlanilhaVinculada> CriarPlanilhaAsync(
        PedidoMovimento pedido, IReadOnlyList<ItemPedido> itens, CancellationToken ct = default);

    /// <summary>Linhas de itens da aba Cotação, como estão agora.</summary>
    Task<IReadOnlyList<LinhaCotacao>> LerLinhasAsync(string spreadsheetId, CancellationToken ct = default);

    /// <summary>Grava o status no nome do arquivo e na aba Controle (só o que estiver diferente).</summary>
    Task<PlanilhaVinculada> AtualizarStatusAsync(
        PlanilhaVinculada planilha, string status, CancellationToken ct = default);

    /// <summary>Marca como Cotação Importada e deixa o link somente leitura (seção 17).</summary>
    Task<PlanilhaVinculada> MarcarComoImportadaAsync(PlanilhaVinculada planilha, CancellationToken ct = default);

    /// <summary>Move para "Registros Arquivados", renomeia e revoga o link público (seção 19.1).</summary>
    Task ArquivarAsync(PlanilhaVinculada planilha, CancellationToken ct = default);

    /// <summary>Todas as planilhas, ativas e arquivadas, mais recentes primeiro.</summary>
    Task<IReadOnlyList<RegistroHistorico>> ListarHistoricoAsync(CancellationToken ct = default);

    /// <summary>
    /// A planilha como está agora no Drive; null se não existir mais, estiver na lixeira ou tiver saído das
    /// pastas da aplicação.
    /// </summary>
    Task<RegistroHistorico?> ObterRegistroAsync(string spreadsheetId, CancellationToken ct = default);

    /// <summary>
    /// Envia para a lixeira do Drive (restaurável por 30 dias). Antes revoga o link público e, se estiver ativa,
    /// arquiva: restaurada, ela volta como arquivada e não disputa com a planilha ativa do pedido.
    /// </summary>
    Task EnviarParaLixeiraAsync(RegistroHistorico planilha, CancellationToken ct = default);
}
